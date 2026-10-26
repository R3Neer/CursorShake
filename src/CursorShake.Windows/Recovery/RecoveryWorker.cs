using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace CursorShake.Windows.Recovery;

public static class RecoveryWorker
{
    public static RestoreResult Run(string sessionDirectory)
    {
        RecoveryManifest? manifest = null;
        try
        {
            manifest = BaselineStore.ReadManifest(sessionDirectory);
            using var current = Process.GetCurrentProcess();
            if (manifest.OwnerSid != WindowsIdentity.GetCurrent().User?.Value || manifest.SessionId != current.SessionId) return new(false, ["Recovery user or interactive session differs from the owner."]);
            using var ledger = new OwnershipLedger(manifest.LedgerName, manifest.Cursors.Count, false);
            using var ready = EventWaitHandle.OpenExisting(manifest.ReadyName);
            using var stop = EventWaitHandle.OpenExisting(manifest.StopName);
            Process? parent = null;
            try
            {
                try { parent = Process.GetProcessById(manifest.ParentPid); }
                catch (ArgumentException) { }
                if (parent is not null && parent.StartTime.ToUniversalTime().Ticks != manifest.ParentStartTicks) return new(false, ["Parent process identity changed; recovery abstained."]);
                // Acquiring the handle before Ready prevents PID reuse while we wait.
                using var exited = parent is null ? null : new ProcessExitWaitHandle(parent.SafeHandle);
                ready.Set();
                if (parent is not null && !parent.HasExited)
                {
                    int signaled = WaitHandle.WaitAny([exited!, stop]);
                    if (signaled == 1 && !ledger.Disarmed) parent.WaitForExit();
                }
                using var recoveryLease = new RecoveryLease(manifest);
                if (!BaselineStore.ExistsWithIdentity(sessionDirectory, manifest)) return new(true, []);
                var result = ledger.Disarmed ? new RestoreResult(true, []) : RecoveryRestorer.Restore(sessionDirectory, manifest, ledger);
                if (result.AllOwnedRestored) BaselineStore.Cleanup(sessionDirectory, manifest);
                else BaselineStore.WriteResult(sessionDirectory, manifest, result);
                return result;
            }
            finally { parent?.Dispose(); }
        }
        catch (Exception e)
        {
            var result = new RestoreResult(false, ["Recovery failed without forcing an unverified cursor: " + e.Message]);
            if (manifest is not null) { try { BaselineStore.WriteResult(sessionDirectory, manifest, result); } catch { } }
            return result;
        }
    }
    private sealed class ProcessExitWaitHandle : WaitHandle
    {
        public ProcessExitWaitHandle(SafeProcessHandle process) => SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), false);
    }
}
