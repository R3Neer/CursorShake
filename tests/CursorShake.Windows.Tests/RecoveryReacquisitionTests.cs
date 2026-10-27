using System.Reflection;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;
using Microsoft.Win32;
namespace CursorShake.Windows.Tests;

public sealed class RecoveryReacquisitionTests
{
    [Fact] public void SettingsSnapshotPreservesDefaultValueBaseSizeAndValueKinds()
    {
        var original=Snapshot(new CursorRegistryValue("",RegistryValueKind.String,"Windows Default"),new CursorRegistryValue("CursorBaseSize",RegistryValueKind.DWord,32));
        Assert.False(original.Equals(Snapshot(new CursorRegistryValue("",RegistryValueKind.String,"Other"),new CursorRegistryValue("CursorBaseSize",RegistryValueKind.DWord,32))));
        Assert.False(original.Equals(Snapshot(new CursorRegistryValue("",RegistryValueKind.String,"Windows Default"),new CursorRegistryValue("CursorBaseSize",RegistryValueKind.DWord,48))));
        Assert.False(original.Equals(Snapshot(new CursorRegistryValue("",RegistryValueKind.String,"Windows Default"),new CursorRegistryValue("CursorBaseSize",RegistryValueKind.QWord,32L))));
        Assert.False(original.Equals(Snapshot(new CursorRegistryValue("CursorBaseSize",RegistryValueKind.DWord,32))));
    }
    [Fact] public void SettingsSnapshotRejectsAddedValuesAndKeyExistenceChanges()
    {
        var empty=CursorSettingsSnapshot.FromValues(true,[]);
        Assert.False(empty.Equals(CursorSettingsSnapshot.FromValues(false,[])));
        Assert.False(empty.Equals(Snapshot(new CursorRegistryValue("extra",RegistryValueKind.String,"x"))));
    }
    [Fact] public void NamesAreCaseInsensitiveButValuesAndMultiStringOrderAreExact()
    {
        var one=Snapshot(new CursorRegistryValue("Arrow",RegistryValueKind.String,"A"),new CursorRegistryValue("list",RegistryValueKind.MultiString,new[]{"a","b"}));
        var same=Snapshot(new CursorRegistryValue("LIST",RegistryValueKind.MultiString,new[]{"a","b"}),new CursorRegistryValue("arrow",RegistryValueKind.String,"A"));
        Assert.True(one.Equals(same));Assert.Equal(one.GetHashCode(),same.GetHashCode());
        Assert.False(one.Equals(Snapshot(new CursorRegistryValue("Arrow",RegistryValueKind.String,"a"),new CursorRegistryValue("list",RegistryValueKind.MultiString,new[]{"a","b"}))));
        Assert.False(one.Equals(Snapshot(new CursorRegistryValue("Arrow",RegistryValueKind.String,"A"),new CursorRegistryValue("list",RegistryValueKind.MultiString,new[]{"b","a"}))));
    }
    [Fact] public void SnapshotArraysAreImmutableAndExpandStringsRemainUnexpanded()
    {
        byte[] binary=[1,2];string[] text=["original"];
        var original=Snapshot(new CursorRegistryValue("binary",RegistryValueKind.Binary,binary),new CursorRegistryValue("multi",RegistryValueKind.MultiString,text));
        binary[0]=9;text[0]="changed";
        Assert.True(original.Equals(Snapshot(new CursorRegistryValue("binary",RegistryValueKind.Binary,new byte[]{1,2}),new CursorRegistryValue("multi",RegistryValueKind.MultiString,new[]{"original"}))));
        Assert.False(Snapshot(new CursorRegistryValue("Arrow",RegistryValueKind.ExpandString,@"%SystemRoot%\Cursors\arrow.cur")).Equals(Snapshot(new CursorRegistryValue("Arrow",RegistryValueKind.ExpandString,@"C:\Windows\Cursors\arrow.cur"))));
        Assert.False(Snapshot(new CursorRegistryValue("Arrow",RegistryValueKind.ExpandString,"same")).Equals(Snapshot(new CursorRegistryValue("Arrow",RegistryValueKind.String,"same"))));
    }
    [Fact] public void InvalidKindsTypesAndDuplicateNamesFailClosed()
    {
        Assert.Throws<InvalidDataException>(()=>Snapshot(new CursorRegistryValue("value",RegistryValueKind.Unknown,"unknown")));
        Assert.Throws<InvalidDataException>(()=>Snapshot(new CursorRegistryValue("value",RegistryValueKind.DWord,32L)));
        Assert.Throws<InvalidDataException>(()=>Snapshot(new CursorRegistryValue("value",RegistryValueKind.String,"a"),new CursorRegistryValue("VALUE",RegistryValueKind.String,"b")));
        Assert.Throws<InvalidDataException>(()=>CursorSettingsSnapshot.FromValues(false,[new CursorRegistryValue("value",RegistryValueKind.String,"a")]));
    }
    [Fact] [Trait("Category","NativeReadOnly")]
    public void BaselineCannotBeReacquiredWithoutTheRequiredLiveGuard()
    {
        using var fixture=new CursorFixture();using var session=new NativeCursorSession(fixture.DirectoryPath,"unused",startGuard:false);
        Ledger(session).SetExternalChange();
        Assert.False(session.TryReacquireUnchangedBaseline());Assert.True(session.IsExternalChangeDetected);
    }
    [Fact] [Trait("Category","NativeReadOnly")]
    public void VerifiedUnchangedBaselineReacquiresAsAllRestoredAndRetainsFirstObservationPerIncident()
    {
        using var fixture=new CursorFixture();using var session=new NativeCursorSession(fixture.DirectoryPath,GuardExecutable());var ledger=Ledger(session);
        // Corrupt only this private ledger's expected hash: desktop cursors and registry are never written.
        byte[] other=Enumerable.Repeat((byte)0xA5,32).ToArray();ledger.Write(0,OwnershipPhase.Confirmed,other,other);
        Assert.True(session.CheckForExternalChanges("fixture first inspection"));string diagnostic=Assert.IsType<string>(session.ExternalChangeDiagnostic);
        Assert.Contains("Context=fixture first inspection",diagnostic);
        Assert.True(session.CheckForExternalChanges("later inspection of the same incident"));Assert.Equal(diagnostic,session.ExternalChangeDiagnostic);
        Assert.Contains("Role=Arrow",diagnostic);Assert.Contains("LedgerGeneration=1",diagnostic);Assert.Contains("ActualHandle=",diagnostic);Assert.Contains("ActualSHA256=",diagnostic);
        int commitChecks=0;
        Assert.False(session.TryReacquireUnchangedBaseline(()=>{commitChecks++;return false;}));
        Assert.Equal(1,commitChecks);Assert.True(session.IsExternalChangeDetected);
        Assert.True(session.TryReacquireUnchangedBaseline());Assert.False(session.IsExternalChangeDetected);
        for(int i=0;i<CursorRoles.All.Count;i++)
        {
            var owned=ledger.Read(i);Assert.Equal(OwnershipPhase.Restored,owned.Phase);
            byte[] baseline=Convert.FromHexString(CursorFingerprint.Of(session.BaselinePixels[CursorRoles.All[i]]));
            Assert.Equal(baseline,owned.PreviousHash);Assert.Equal(baseline,owned.PendingHash);
        }
        ledger.Write(1,OwnershipPhase.Confirmed,other,other);Assert.True(session.CheckForExternalChanges());
        Assert.NotEqual(diagnostic,session.ExternalChangeDiagnostic);Assert.Contains("Role=Text",session.ExternalChangeDiagnostic!);Assert.True(session.TryReacquireUnchangedBaseline());
    }
    [Fact] [Trait("Category","NativeReadOnly")]
    public void AGuardThatWasReadyButHasExitedCannotAuthorizeReacquisition()
    {
        using var fixture = new CursorFixture();
        using var session = new NativeCursorSession(fixture.DirectoryPath, GuardExecutable());
        var ledger = Ledger(session);
        string directory = Directory.EnumerateDirectories(Path.Combine(fixture.DirectoryPath, "recovery")).Single();
        var manifest = BaselineStore.ReadManifest(directory);
        var guard = (System.Diagnostics.Process)typeof(NativeCursorSession).GetField("_guard", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        // Retire only this fixture's untouched ledger, then let its own helper exit cooperatively.
        // No cursor, registry value or unrelated process is modified or terminated.
        ledger.Disarm();
        using (var stop = EventWaitHandle.OpenExisting(manifest.StopName)) stop.Set();
        Assert.True(guard.WaitForExit(10000), "The fixture's own disarmed helper did not exit.");
        ledger.SetExternalChange();
        Assert.False(session.TryReacquireUnchangedBaseline());
        Assert.True(session.IsExternalChangeDetected);
        foreach (var role in CursorRoles.All)
            Assert.Equal(CursorFingerprint.Of(session.BaselinePixels[role]), Convert.ToHexString(CursorFingerprint.Current(role)));
    }    private static CursorSettingsSnapshot Snapshot(params CursorRegistryValue[] values)=>CursorSettingsSnapshot.FromValues(true,values);
    private static OwnershipLedger Ledger(NativeCursorSession session)=>(OwnershipLedger)typeof(NativeCursorSession).GetField("_ledger",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;
    private static string GuardExecutable()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"CursorShake.slnx")))root=root.Parent;
        Assert.NotNull(root);string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string path=Path.Combine(root!.FullName,"src","CursorShake.RecoveryGuard","bin",configuration,"net10.0-windows","CursorShake.RecoveryGuard.exe");
        Assert.True(File.Exists(path),"Build the matching solution/guard before exclusive read-only native tests: "+path);return path;
    }
}
