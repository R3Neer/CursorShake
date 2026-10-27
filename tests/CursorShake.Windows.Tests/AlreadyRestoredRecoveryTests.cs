using CursorShake.Windows.Configuration;
using System.Diagnostics;
using System.Reflection;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;
namespace CursorShake.Windows.Tests;
[Trait("Category","NativeReadOnly")]
public sealed class AlreadyRestoredRecoveryTests
{
    [Fact] public void ExternalFlagDoesNotMakeAnAlreadyOriginalDesktopAnError()
    {
        using var fixture=new CursorFixture();
        using var session=new NativeCursorSession(fixture.DirectoryPath,"unused",startGuard:false);
        var ledger=(OwnershipLedger)typeof(NativeCursorSession).GetField("_ledger",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;
        var before=CursorRoles.All.ToDictionary(role=>role,role=>Convert.ToHexString(CursorFingerprint.Current(role)));
        ledger.SetExternalChange();
        var result=session.RestoreOwned();
        Assert.True(result.AllOwnedRestored);Assert.Empty(result.Diagnostics);
        Assert.True(session.IsExternalChangeDetected); // No readoption or reset of the external flag.
        foreach(var role in CursorRoles.All)Assert.Equal(before[role],Convert.ToHexString(CursorFingerprint.Current(role)));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExplicitDeadParentRecoveryNeedsNoLedgerOnlyWhenEveryOriginalRoleIsPresent(bool mismatch)
    {
        using var fixture=new CursorFixture();string directory=Path.Combine(fixture.DirectoryPath,Guid.NewGuid().ToString("N"));
        RecoveryManifest manifest;
        using(var construction=new RecoveryConstruction(directory)){manifest=BaselineStore.Capture(directory,construction,null,out _,out _);construction.Complete();}
        var roles=manifest.Cursors.ToArray();
        if(mismatch)roles[0]=roles[0] with {Fingerprint=new string('0',64)};
        manifest=manifest with {ParentPid=int.MaxValue,Cursors=roles};
        DurableState.Write(Path.Combine(directory,"session.json"),manifest);
        var result=RecoveryBootstrap.TryRetireAlreadyRestored(directory);
        Assert.Equal(!mismatch,result.AllOwnedRestored);
        Assert.Equal(mismatch,Directory.Exists(directory));
    }
}
