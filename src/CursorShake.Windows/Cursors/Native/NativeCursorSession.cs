using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Recovery;
namespace CursorShake.Windows.Cursors.Native;

/// <summary>Owns baseline, shared role identities and verified native mutation for one interactive lifetime.</summary>
public sealed class NativeCursorSession : IDisposable
{
    private readonly object _sync = new();
    private readonly RecoveryManifest _manifest = null!;
    private readonly OwnershipLedger _ledger = null!;
    private readonly Action<RecoveryCheckpoint>? _checkpoint;
    private readonly string _sessionDirectory;
    private readonly Process? _guard;
    private readonly EventWaitHandle? _ready, _stop;
    private readonly bool _guardRequired;
    private bool _disposed;
    public IReadOnlyDictionary<CursorRole, nint> SharedHandles { get; }
    public IReadOnlyDictionary<CursorRole, CursorPixels> BaselinePixels { get; }
    public long BaselineEstimatedBytes { get; }
    public bool IsExternalChangeDetected => _ledger.ExternalChange;

    public NativeCursorSession(string stateDirectory, string guardExecutablePath, bool startGuard = true, Action<RecoveryCheckpoint>? checkpoint = null)
    {
        _guardRequired = startGuard; _checkpoint = checkpoint;
        _sessionDirectory = Path.Combine(Path.GetFullPath(stateDirectory), "recovery", Guid.NewGuid().ToString("N"));
        RecoveryConstruction? construction = null;
        try
        {
            construction = new RecoveryConstruction(_sessionDirectory);
            _checkpoint?.Invoke(RecoveryCheckpoint.SessionDirectoryCreated);
            _manifest = BaselineStore.Capture(_sessionDirectory, construction, checkpoint, out var pixels, out var handles);
            BaselinePixels = pixels; SharedHandles = handles;
            BaselineEstimatedBytes = pixels.Values.Sum(p => p.EstimatedBytes) + _manifest.Cursors.Sum(c => c.SourceBytes);
            _ledger = new OwnershipLedger(_manifest.LedgerName, _manifest.Cursors.Count, true);
            _checkpoint?.Invoke(RecoveryCheckpoint.LedgerCreated);
            if (startGuard)
            {
                if (!File.Exists(guardExecutablePath)) throw new FileNotFoundException("RecoveryGuard executable is required before any cursor modification.", guardExecutablePath);
                _ready = new EventWaitHandle(false, EventResetMode.ManualReset, _manifest.ReadyName);
                _stop = new EventWaitHandle(false, EventResetMode.ManualReset, _manifest.StopName);
                var start = new ProcessStartInfo(Path.GetFullPath(guardExecutablePath)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add("--session"); start.ArgumentList.Add(_sessionDirectory);
                _guard = Process.Start(start) ?? throw new IOException("RecoveryGuard could not start.");
                if (!_ready.WaitOne(TimeSpan.FromSeconds(15)) || _guard.HasExited) throw new IOException("RecoveryGuard did not confirm readiness; no cursor was modified.");
                _checkpoint?.Invoke(RecoveryCheckpoint.GuardReady);
            }
            construction.Complete();
        }
        catch
        {
            // No publication is possible before the constructor returns successfully.
            construction?.ReleaseProtection();
            try { _ledger?.Disarm(); } catch { }
            try { _stop?.Set(); } catch { }
            if (_guard is not null) { try { _guard.WaitForExit(2000); } catch { } _guard.Dispose(); }
            _ready?.Dispose(); _stop?.Dispose(); _ledger?.Dispose();
            construction?.Cleanup();
            throw;
        }
        finally { construction?.Dispose(); }
    }

    public void Publish(CursorRole role, CursorPixels pixels)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_guardRequired || _guard is null || _guard.HasExited) throw new InvalidOperationException("A ready recovery process is required before native cursor modification.");
            if (IsExternalChangeDetected) throw new InvalidOperationException("Cursor ownership was ceded after an external change.");
            int index = Index(role); var baseline = _manifest.Cursors[index];
            var owned = _ledger.Read(index); byte[] current = CursorFingerprint.Current(role);
            bool expected = owned.Phase is OwnershipPhase.Untouched or OwnershipPhase.Restored ? Convert.ToHexString(current) == baseline.Fingerprint : current.AsSpan().SequenceEqual(owned.PendingHash);
            if (!expected || CursorNative.LoadCursor(0, (nint)(uint)role) != SharedHandles[role])
            { _ledger.SetExternalChange(); throw new InvalidOperationException("An external cursor change was detected; the utility stopped modifying the scheme."); }
            using var created = CursorNative.Create(pixels);
            byte[] next = CursorFingerprint.Bytes(CursorNative.Capture(created.DangerousGetHandle()));
            _ledger.Write(index, OwnershipPhase.Pending, current, next);
            _checkpoint?.Invoke(RecoveryCheckpoint.BeforeNativeMutation);
            if (!CursorNative.SetSystemCursor(created.Transfer(), (uint)role)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Publish cursor role " + role);
            _checkpoint?.Invoke(RecoveryCheckpoint.AfterNativeMutation);
            if (!CursorFingerprint.Current(role).AsSpan().SequenceEqual(next))
            { _ledger.SetExternalChange(); throw new InvalidOperationException("Published cursor verification failed; ownership was ceded."); }
            _ledger.Write(index, OwnershipPhase.Confirmed, next, next);
        }
    }

    public bool CheckForExternalChanges()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ledger.ExternalChange) return true;
            for (int i = 0; i < _manifest.Cursors.Count; i++)
            {
                var role = _manifest.Cursors[i]; var state = _ledger.Read(i); byte[] current = CursorFingerprint.Current(role.Role);
                bool expected = state.Phase is OwnershipPhase.Untouched or OwnershipPhase.Restored ? Convert.ToHexString(current) == role.Fingerprint : current.AsSpan().SequenceEqual(state.PendingHash) || current.AsSpan().SequenceEqual(state.PreviousHash);
                if (!expected || CursorNative.LoadCursor(0, (nint)(uint)role.Role) != SharedHandles[role.Role]) { _ledger.SetExternalChange(); return true; }
            }
            return false;
        }
    }
    public RestoreResult RestoreOwned()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RestoreResult result;
            try { _checkpoint?.Invoke(RecoveryCheckpoint.BeforeRestore); using var lease = new RecoveryLease(_manifest); result = RecoveryRestorer.Restore(_sessionDirectory, _manifest, _ledger); }
            catch (Exception e) { result = new(false, ["Restoration could not be verified: " + e.Message]); }
            if (!result.AllOwnedRestored)
            {
                try { _checkpoint?.Invoke(RecoveryCheckpoint.BeforeResultWrite); BaselineStore.WriteResult(_sessionDirectory, _manifest, result); }
                catch (Exception e) { result = new(false, result.Diagnostics.Concat(["Recovery status could not be written: " + e.Message]).ToArray()); }
            }
            return result;
        }
    }
    private int Index(CursorRole role)
    { for (int i = 0; i < _manifest.Cursors.Count; i++) if (_manifest.Cursors[i].Role == role) return i; throw new ArgumentOutOfRangeException(nameof(role)); }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            var result = new RestoreResult(false, ["Restoration did not complete."]);
            try
            {
                result = RestoreOwned();
                if (result.AllOwnedRestored) _ledger.Disarm();
                _stop?.Set();
                if (_guard is not null)
                {
                    // If restoration failed, the helper remains armed until actual parent death.
                    if (result.AllOwnedRestored) _guard.WaitForExit(5000);
                }
                else if (result.AllOwnedRestored) BaselineStore.Cleanup(_sessionDirectory, _manifest);
            }
            catch (Exception e)
            {
                try { BaselineStore.WriteResult(_sessionDirectory, _manifest, new(false, result.Diagnostics.Concat(["Teardown: " + e.Message]).ToArray())); } catch { }
            }
            finally
            {
                _disposed = true;
                try { _stop?.Set(); } catch { }
                _guard?.Dispose(); _ready?.Dispose(); _stop?.Dispose(); _ledger.Dispose();
            }
        }
    }
}
