using System.Diagnostics;
using System.Security.Principal;
using CursorShake.Windows.Configuration;
namespace CursorShake.Windows.Recovery;

/// <summary>Inspects previous receipts before a new native baseline is allowed. Inspection never restores anything.</summary>
public static partial class RecoveryBootstrap
{
    public static IReadOnlyList<PendingRecovery> Inspect(string stateDirectory)
    {
        string root = Path.Combine(Path.GetFullPath(stateDirectory), "recovery");
        var pending = new List<PendingRecovery>();
        try
        {
            using var protection = DirectoryProtection.Acquire(root);
            using var current = Process.GetCurrentProcess();
            string? sid = WindowsIdentity.GetCurrent().User?.Value;
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
                try
                {
                    var manifest = BaselineStore.ReadManifest(directory);
                    if (manifest.OwnerSid != sid) { pending.Add(new(directory, false, false, true, "Recovery receipt ownership cannot be verified; no new baseline should be captured.")); continue; }
                    bool otherSession = manifest.SessionId != current.SessionId;
                    bool alive = ParentIsAlive(manifest);
                    bool ledgerAvailable;
                    try { using var ledger = new OwnershipLedger(manifest.LedgerName, manifest.Cursors.Count, false); ledgerAvailable = true; }
                    catch (FileNotFoundException) { ledgerAvailable = false; }
                    catch (IOException) { ledgerAvailable = false; }
                    string summary = otherSession ? "Recovery receipt belongs to another interactive session; it was left untouched."
                        : alive ? "The original process is still alive; its cursor ownership must not be adopted."
                        : ledgerAvailable ? "A previous process ended while recovery ownership evidence is still available. Explicit verified recovery can be attempted."
                        : "The previous ownership ledger is gone. No cursor was restored and a fresh baseline would be unsafe; restore the intended scheme explicitly before resolving this receipt.";
                    pending.Add(new(directory, alive, ledgerAvailable, !otherSession, summary));
                }
                catch (Exception e) { pending.Add(new(directory, false, false, true, "Recovery state cannot be verified: " + e.Message)); }
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception e) { pending.Add(new(root, false, false, true, "Recovery state cannot be inspected: " + e.Message)); }
        return pending;
    }

    public static RestoreResult TryRecover(string sessionDirectory)
    {
        try
        {
            if (!SessionExists(sessionDirectory)) return new(true, []);
            var manifest = BaselineStore.ReadManifest(sessionDirectory);
            using var current = Process.GetCurrentProcess();
            if (manifest.OwnerSid != WindowsIdentity.GetCurrent().User?.Value || manifest.SessionId != current.SessionId) return new(false, ["Recovery belongs to another user or desktop session; it was left untouched."]);
            using var lease = new RecoveryLease(manifest);
            // A competing guard can complete cleanup while this operation waits for the lock.
            if (!SessionExists(sessionDirectory)) return new(true, []);
            manifest = BaselineStore.ReadManifest(sessionDirectory);
            if (ParentIsAlive(manifest)) return new(false, ["The original process is still alive; recovery was not attempted."]);
            OwnershipLedger ledger;
            try { ledger = new OwnershipLedger(manifest.LedgerName, manifest.Cursors.Count, false); }
            catch (FileNotFoundException) { return new(false, ["Ownership evidence no longer exists. Recovery did not guess or overwrite the current cursor scheme."]); }
            using (ledger)
            {
                var result = RecoveryRestorer.Restore(sessionDirectory, manifest, ledger);
                if (result.AllOwnedRestored) BaselineStore.Cleanup(sessionDirectory, manifest);
                else { try { BaselineStore.WriteResult(sessionDirectory, manifest, result); } catch { } }
                return result;
            }
        }
        catch (Exception e) { return new(false, ["Recovery could not be verified: " + e.Message]); }
    }

    private static bool SessionExists(string directory)
    {
        try { using var protection = DirectoryProtection.Acquire(directory); return true; }
        catch (FileNotFoundException) { return false; }
    }
    internal static bool ParentIsAlive(RecoveryManifest manifest)
    {
        try
        {
            using var parent = Process.GetProcessById(manifest.ParentPid);
            return !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == manifest.ParentStartTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
