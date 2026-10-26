using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace CursorShake.Windows.Recovery;

public static class RecoveryWorker
{
    public static RestoreResult Run(string sessionDirectory)
    {
        RecoveryManifest? manifest = null;
        bool verifiedOwner = false;
        string stage = "ReadManifest";
        try
        {
            manifest = BaselineStore.ReadManifest(sessionDirectory);
            stage = "VerifyUserAndSession";
            using var current = Process.GetCurrentProcess();
            string? sid = WindowsIdentity.GetCurrent().User?.Value;
            if (manifest.OwnerSid != sid || manifest.SessionId != current.SessionId)
                throw new UnauthorizedAccessException($"Recovery user/session differs from its owner. Expected SID={manifest.OwnerSid}, session={manifest.SessionId}; actual SID={sid}, session={current.SessionId}. No foreign recovery status was modified.");
            verifiedOwner = true;
            stage = "OpenOwnershipLedger";
            using var ledger = new OwnershipLedger(manifest.LedgerName, manifest.Cursors.Count, false);
            stage = "OpenReadySignal";
            using var ready = EventWaitHandle.OpenExisting(manifest.ReadyName);
            stage = "OpenStopSignal";
            using var stop = EventWaitHandle.OpenExisting(manifest.StopName);
            Process? parent = null;
            try
            {
                stage = "FindParent";
                try { parent = Process.GetProcessById(manifest.ParentPid); }
                catch (ArgumentException) { }
                stage = "VerifyParentIdentity";
                if (parent is not null && parent.StartTime.ToUniversalTime().Ticks != manifest.ParentStartTicks)
                    throw new InvalidOperationException($"Parent process identity changed; recovery abstained. PID={manifest.ParentPid}, expected StartTicks={manifest.ParentStartTicks}, actual StartTicks={parent.StartTime.ToUniversalTime().Ticks}.");
                // Acquiring the handle before Ready prevents PID reuse while we wait.
                stage = "BindParentExitHandle";
                using var exited = parent is null ? null : new ProcessExitWaitHandle(parent.SafeHandle);
                stage = "SignalReady";
                ready.Set();
                if (parent is not null && !parent.HasExited)
                {
                    stage = "WaitForParentOrStop";
                    int signaled = WaitHandle.WaitAny([exited!, stop]);
                    if (signaled == 1 && !ledger.Disarmed) parent.WaitForExit();
                }
                stage = "AcquireRecoveryLease";
                using var recoveryLease = new RecoveryLease(manifest);
                stage = "RevalidateSession";
                if (!BaselineStore.ExistsWithIdentity(sessionDirectory, manifest)) return new(true, []);
                stage = "RestoreOrDisarmed";
                var result = ledger.Disarmed ? new RestoreResult(true, []) : RecoveryRestorer.Restore(sessionDirectory, manifest, ledger);
                stage = result.AllOwnedRestored ? "CleanupVerifiedSession" : "WriteRecoveryResult";
                if (result.AllOwnedRestored) BaselineStore.Cleanup(sessionDirectory, manifest);
                else BaselineStore.WriteResult(sessionDirectory, manifest, result);
                return result;
            }
            finally { parent?.Dispose(); }
        }
        catch (Exception e)
        {
            var result = new RestoreResult(false, [$"Recovery failed at stage '{stage}' without forcing an unverified cursor:\n{e}"]);
            if (manifest is not null && verifiedOwner)
            {
                try { BaselineStore.WriteResult(sessionDirectory, manifest, result); }
                catch (Exception statusError) { result = new(false, result.Diagnostics.Concat([$"The verified guard diagnostic could not be persisted safely:\n{statusError}"]).ToArray()); }
            }
            return result;
        }
    }
    private sealed class ProcessExitWaitHandle : WaitHandle
    {
        public ProcessExitWaitHandle(SafeProcessHandle process) => SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), false);
    }
}
