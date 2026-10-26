using System.Diagnostics;
using System.Security.Principal;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;
namespace CursorShake.Windows.Tests;

public sealed class RecoveryDiagnosticsTests
{
    [Fact] public void MissingLedgerReportsItsStageAndFullExceptionInTheVerifiedReceipt()
    {
        using var f=new Fixture();var result=RecoveryWorker.Run(f.Session);
        Assert.False(result.AllOwnedRestored);string detail=Assert.Single(result.Diagnostics);
        Assert.Contains("OpenOwnershipLedger",detail);Assert.Contains("System.IO.FileNotFoundException",detail);Assert.Contains("RecoveryWorker.Run",detail);
        var stored=DurableState.Read<RestoreResult>(Path.Combine(f.Session,"result.json"));Assert.Equal(detail,Assert.Single(stored.Diagnostics));
    }
    [Fact] public void ForeignOwnerFailureDoesNotWriteIntoTheOtherIdentitysReceipt()
    {
        using var f=new Fixture();f.Set(f.Manifest with{OwnerSid="S-1-0-0"});string original=File.ReadAllText(Path.Combine(f.Session,"session.json"));
        var result=RecoveryWorker.Run(f.Session);Assert.False(result.AllOwnedRestored);Assert.Contains("VerifyUserAndSession",Assert.Single(result.Diagnostics));
        Assert.Contains("UnauthorizedAccessException",result.Diagnostics[0]);Assert.False(File.Exists(Path.Combine(f.Session,"result.json")));
        Assert.Equal(original,File.ReadAllText(Path.Combine(f.Session,"session.json")));
    }
    [Fact] public void ParentIdentityFailureIsPersistedWithoutSignalingReady()
    {
        using var f=new Fixture();f.Set(f.Manifest with{ParentStartTicks=f.Manifest.ParentStartTicks-1});
        using var ledger=new OwnershipLedger(f.Manifest.LedgerName,f.Manifest.Cursors.Count,true);
        using var ready=new EventWaitHandle(false,EventResetMode.ManualReset,f.Manifest.ReadyName);
        using var stop=new EventWaitHandle(false,EventResetMode.ManualReset,f.Manifest.StopName);
        var result=RecoveryWorker.Run(f.Session);Assert.False(result.AllOwnedRestored);Assert.False(ready.WaitOne(0));
        Assert.Contains("VerifyParentIdentity",Assert.Single(result.Diagnostics));Assert.Contains("expected StartTicks",result.Diagnostics[0]);
        Assert.Equal(result.Diagnostics[0],Assert.Single(DurableState.Read<RestoreResult>(Path.Combine(f.Session,"result.json")).Diagnostics));
    }
    [Fact] public void ReadinessFailureIncludesExitOutcomeAndOnlyTheVerifiedGuardDiagnostic()
    {
        using var f=new Fixture();DurableState.Write(Path.Combine(f.Session,"result.json"),new RestoreResult(false,["stage fixture: inner failure"]));
        var error=NativeCursorSession.GuardReadinessFailure(f.Session,f.Manifest,"guard exited before Ready",123,3);
        Assert.Contains("guard exited before Ready",error.Message);Assert.Contains("PID=123",error.Message);Assert.Contains("ExitCode=3",error.Message);
        Assert.Contains("stage fixture: inner failure",error.Message);Assert.Contains("no cursor was modified",error.Message);
    }
    [Fact] public void DiagnosticFromAChangedManifestIsNeverAdopted()
    {
        using var f=new Fixture();var expected=f.Manifest;f.Set(f.Manifest with{ParentStartTicks=f.Manifest.ParentStartTicks+1});
        DurableState.Write(Path.Combine(f.Session,"result.json"),new RestoreResult(false,["UNTRUSTED-DETAIL"]));
        var error=NativeCursorSession.GuardReadinessFailure(f.Session,expected,"15-second readiness timeout",123,null);
        Assert.Contains("still running",error.Message);Assert.Contains("identity changed",error.Message);Assert.DoesNotContain("UNTRUSTED-DETAIL",error.Message);
    }
    [Fact] public void MissingGuardDiagnosticDoesNotReplaceTheReadinessFailure()
    {
        using var f=new Fixture();var error=NativeCursorSession.GuardReadinessFailure(f.Session,f.Manifest,"15-second readiness timeout",123,null);
        Assert.Contains("15-second readiness timeout",error.Message);Assert.Contains("absent",error.Message);Assert.IsType<IOException>(error);
    }
    private sealed class Fixture:IDisposable
    {
        private readonly CursorFixture files=new();
        public string Session{get;}public RecoveryManifest Manifest{get;private set;}
        public Fixture()
        {
            string nonce=Guid.NewGuid().ToString("N");Session=Path.Combine(files.DirectoryPath,nonce);Directory.CreateDirectory(Session);
            using var parent=Process.GetCurrentProcess();
            var roles=CursorRoles.All.Select(role=>new BaselineCursor(role,1,((uint)role)+".pixels",new string('A',64),null,null,0)).ToArray();
            Manifest=new(nonce,parent.Id,parent.StartTime.ToUniversalTime().Ticks,parent.SessionId,WindowsIdentity.GetCurrent().User!.Value,roles);Set(Manifest);
        }
        public void Set(RecoveryManifest manifest){Manifest=manifest;DurableState.Write(Path.Combine(Session,"session.json"),manifest);}
        public void Dispose()=>files.Dispose();
    }
}
