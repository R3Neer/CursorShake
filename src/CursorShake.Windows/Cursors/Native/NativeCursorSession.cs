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
    private readonly CursorSettingsSnapshot _settingsBaseline=null!;
    private string? firstExternalChangeDiagnostic;
    private bool _disposed;
    public IReadOnlyDictionary<CursorRole, nint> SharedHandles { get; }
    public IReadOnlyDictionary<CursorRole, CursorPixels> BaselinePixels { get; }
    public long BaselineEstimatedBytes { get; }
    public bool IsExternalChangeDetected => _ledger.ExternalChange;
    public string? ExternalChangeDiagnostic {get{lock(_sync)return firstExternalChangeDiagnostic;}}

    public NativeCursorSession(string stateDirectory, string guardExecutablePath, bool startGuard = true, Action<RecoveryCheckpoint>? checkpoint = null)
    {
        _guardRequired = startGuard; _checkpoint = checkpoint;
        _sessionDirectory = Path.Combine(Path.GetFullPath(stateDirectory), "recovery", Guid.NewGuid().ToString("N"));
        RecoveryConstruction? construction = null;
        try
        {
            _settingsBaseline=CursorSettingsSnapshot.Capture();
            construction = new RecoveryConstruction(_sessionDirectory);
            _checkpoint?.Invoke(RecoveryCheckpoint.SessionDirectoryCreated);
            _manifest = BaselineStore.Capture(_sessionDirectory, construction, checkpoint, out var pixels, out var handles);
            if(!_settingsBaseline.Equals(CursorSettingsSnapshot.Capture()))throw new IOException("Cursor registry settings changed while capturing the baseline; no cursor was modified.");
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
                using var guardExited = new GuardExitWaitHandle(_guard.SafeHandle);
                int readiness = WaitHandle.WaitAny([_ready, guardExited], TimeSpan.FromSeconds(15));
                bool exited = _guard.HasExited;
                if (readiness != 0 || exited)
                {
                    string outcome = readiness == WaitHandle.WaitTimeout ? "15-second readiness timeout" : readiness == 1 ? "guard exited before Ready" : "Ready was signaled but the guard had already exited";
                    throw GuardReadinessFailure(_sessionDirectory, _manifest, outcome, _guard.Id, exited ? _guard.ExitCode : null);
                }
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

    internal static IOException GuardReadinessFailure(string directory, RecoveryManifest expected, string outcome, int pid, int? exitCode)
    {
        string status;
        try
        {
            using var protection = BaselineStore.ProtectExistingSession(directory);
            if (protection is null) status = "The recovery receipt directory is absent.";
            else if (!BaselineStore.ExistsWithIdentity(directory, expected)) status = "The verified recovery receipt was retired.";
            else
            {
                using var file = ProtectedFile.Open(Path.Combine(directory, "result.json"));
                var result = System.Text.Json.JsonSerializer.Deserialize<RestoreResult>(file.ReadBytes(128 * 1024), DurableState.Json)
                    ?? throw new InvalidDataException("Guard status is empty.");
                status = "Verified guard result: AllOwnedRestored=" + result.AllOwnedRestored + "\n" + string.Join("\n", result.Diagnostics);
            }
        }
        catch (FileNotFoundException) { status = "A recovery receipt or guard result file is absent; no unverified status was read."; }
        catch (Exception error) { status = "Guard status could not be read from a verified protected receipt:\n" + error; }
        return new IOException($"RecoveryGuard did not confirm readiness; no cursor was modified. Outcome: {outcome}; PID={pid}; ExitCode={(exitCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : "still running")}.\n{status}");
    }
    private sealed class GuardExitWaitHandle : WaitHandle
    {
        public GuardExitWaitHandle(Microsoft.Win32.SafeHandles.SafeProcessHandle process)
            => SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(process.DangerousGetHandle(), false);
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
            nint actualHandle=CursorNative.LoadCursor(0,(nint)(uint)role);
            if (!expected || actualHandle != SharedHandles[role])
            { CedeOwnership("Publication preflight did not match verified ownership",role,current,owned,actualHandle); throw new InvalidOperationException("An external cursor change was detected; the utility stopped modifying the scheme.\n"+ExternalChangeDiagnostic); }
            using var created = CursorNative.Create(pixels);
            byte[] next = CursorFingerprint.Bytes(CursorNative.Capture(created.DangerousGetHandle()));
            _ledger.Write(index, OwnershipPhase.Pending, current, next);
            _checkpoint?.Invoke(RecoveryCheckpoint.BeforeNativeMutation);
            if (!CursorNative.SetSystemCursor(created.Transfer(), (uint)role)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Publish cursor role " + role);
            _checkpoint?.Invoke(RecoveryCheckpoint.AfterNativeMutation);
            byte[] published=CursorFingerprint.Current(role);
            if (!published.AsSpan().SequenceEqual(next))
            { CedeOwnership("Publication verification did not match the newly published cursor",role,published,_ledger.Read(index),CursorNative.LoadCursor(0,(nint)(uint)role)); throw new InvalidOperationException("Published cursor verification failed; ownership was ceded.\n"+ExternalChangeDiagnostic); }
            _ledger.Write(index, OwnershipPhase.Confirmed, next, next);
        }
    }

    public bool CheckForExternalChanges(string? context=null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ledger.ExternalChange) return true;
            for (int i = 0; i < _manifest.Cursors.Count; i++)
            {
                var role = _manifest.Cursors[i]; var state = _ledger.Read(i); byte[] current = CursorFingerprint.Current(role.Role);
                bool expected = state.Phase is OwnershipPhase.Untouched or OwnershipPhase.Restored ? Convert.ToHexString(current) == role.Fingerprint : current.AsSpan().SequenceEqual(state.PendingHash) || current.AsSpan().SequenceEqual(state.PreviousHash);
                nint actualHandle=CursorNative.LoadCursor(0,(nint)(uint)role.Role);
                if (!expected || actualHandle != SharedHandles[role.Role]) { CedeOwnership("Ownership inspection did not match a verified cursor"+(string.IsNullOrWhiteSpace(context)?"":"; Context="+context),role.Role,current,state,actualHandle); return true; }
            }
            return false;
        }
    }
    private void CedeOwnership(string reason,CursorRole role,byte[] actual,OwnershipState owned,nint actualHandle)
    {
        bool alreadyCeded=_ledger.ExternalChange;
        _ledger.SetExternalChange();
        if(!alreadyCeded||firstExternalChangeDiagnostic is null)firstExternalChangeDiagnostic=$"Reason={reason}; Role={role}; Phase={owned.Phase}; LedgerGeneration={owned.Generation}; ActualHandle=0x{actualHandle:X}; ExpectedHandle=0x{SharedHandles[role]:X}; ActualSHA256={Convert.ToHexString(actual)}; BaselineSHA256={_manifest.Cursors[Index(role)].Fingerprint}; PreviousOwnedSHA256={Convert.ToHexString(owned.PreviousHash)}; PendingOwnedSHA256={Convert.ToHexString(owned.PendingHash)}.";
    }
    internal bool TryReacquireUnchangedBaseline(Func<bool>? canCommit=null)
    {
        lock(_sync)
        {
            if(_disposed||!_ledger.ExternalChange||!_guardRequired||_guard is null)return false;
            try
            {
                if(_guard.HasExited||!_settingsBaseline.Equals(CursorSettingsSnapshot.Capture())||!BaselineStillMatches())return false;
                for(int i=0;i<_manifest.Cursors.Count;i++)
                {
                    byte[] hash=Convert.FromHexString(_manifest.Cursors[i].Fingerprint);
                    _ledger.Write(i,OwnershipPhase.Restored,hash,hash);
                }
                if(!BaselineStillMatches()||!_settingsBaseline.Equals(CursorSettingsSnapshot.Capture())||_guard.HasExited)return false;
                // All role slots must be visible as Restored before the recovery reader can observe
                // the cleared flag. Before this commit it must still abstain from cursor writes.
                if(canCommit is not null&&!canCommit())return false;
                Thread.MemoryBarrier();_ledger.ClearExternalChange();return true;
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException or Win32Exception or InvalidOperationException){return false;}
        }
    }
    private bool BaselineStillMatches()
    {
        foreach(var baseline in _manifest.Cursors)
            if(CursorNative.LoadCursor(0,(nint)(uint)baseline.Role)!=SharedHandles[baseline.Role]||Convert.ToHexString(CursorFingerprint.Current(baseline.Role))!=baseline.Fingerprint)return false;
        return true;
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
