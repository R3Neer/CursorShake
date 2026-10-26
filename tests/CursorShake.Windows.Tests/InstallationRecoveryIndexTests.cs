using System.Security.AccessControl;
using System.Security.Principal;
using CursorShake.Windows.Lifecycle;
using CursorShake.Windows.Recovery;
namespace CursorShake.Windows.Tests;

public sealed class InstallationRecoveryIndexTests
{
    [Fact] public void FirstInstallCanPrepareWithoutCreatingAnIndex()
    {
        using var f=new Fixture();InstallationRecoveryIndex.AssertBeforeUpdate(f.App,f.Context);
        Assert.False(Directory.Exists(Path.Combine(f.Data,"CursorShake.Installations")));
    }
    [Fact] public void UpdateWithAnInactiveUsersPendingRecoveryIsBlocked()
    {
        using var f=new Fixture();f.Admit();Directory.CreateDirectory(f.State);f.Pending=[new(f.State,false,false,true,"Unresolved")];
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.AssertBeforeUpdate(f.App,f.Context));
        Assert.Single(Directory.EnumerateFiles(f.Users));
    }
    [Fact] public void UpdateCannotTreatAMissingScopeAsANewInstallation()
    {
        using var f=new Fixture();f.Admit();File.Delete(Path.Combine(f.App,"maintenance-scope.txt"));
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.AssertBeforeUpdate(f.App,f.Context));
        Assert.Single(Directory.EnumerateFiles(f.Users));
    }
    [Fact] public void PortableRegistrationDoesNotCreateIndexButUninstallCannotClaimItClear()
    {
        using var f=new Fixture();InstallationRecoveryIndex.RegisterCurrentUser(f.App,f.State,f.Context);
        Assert.False(Directory.Exists(Path.Combine(f.Data,"CursorShake.Installations")));
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.AssertClear(f.App,f.Context));
    }
    [Fact] public void MissingProvisionedIndexBlocksInstalledAdmission()
    {
        using var f=new Fixture();f.Scope("user");Assert.Throws<FileNotFoundException>(()=>InstallationRecoveryIndex.RegisterCurrentUser(f.App,f.State,f.Context));
    }
    [Fact] public void ProvisionAndRepeatedSameStateAdmissionAreIdempotent()
    {
        using var f=new Fixture();f.Scope("user");InstallationRecoveryIndex.Provision(f.App,f.Context);InstallationRecoveryIndex.Provision(f.App,f.Context);
        InstallationRecoveryIndex.RegisterCurrentUser(f.App,f.State,f.Context);InstallationRecoveryIndex.RegisterCurrentUser(f.App,f.State,f.Context);
        string path=Assert.Single(Directory.EnumerateFiles(f.Users));Assert.Equal(f.Context.Sid.Value+".json",Path.GetFileName(path));
        Assert.Contains(f.State.Replace("\\","\\\\"),File.ReadAllText(path));InstallationRecoveryIndex.AssertClear(f.App,f.Context);
    }
    [Fact] public void ChangedStatePathCannotSilentlyReplaceAUsersRecord()
    {
        using var f=new Fixture();f.Admit();string path=Assert.Single(Directory.EnumerateFiles(f.Users));string before=File.ReadAllText(path);
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.RegisterCurrentUser(f.App,f.State+"-other",f.Context));Assert.Equal(before,File.ReadAllText(path));
    }
    [Fact] public void ExistingUntrustedDirectoryIsNeverRepermissioned()
    {
        using var f=new Fixture();f.Scope("user");string path=Path.Combine(f.Data,"CursorShake.Installations");Directory.CreateDirectory(path);
        string before=new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.Provision(f.App,f.Context));
        Assert.Equal(before,new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All));
    }
    [Fact] public void AllUsersProvisionRequiresElevationBeforeCreatingAnything()
    {
        using var f=new Fixture();f.Scope("all-users");Assert.Throws<IOException>(()=>InstallationRecoveryIndex.Provision(f.App,f.Context));
        Assert.False(Directory.Exists(Path.Combine(f.Data,"CursorShake.Installations")));
    }
    [Fact] public void AnyPendingReceiptBlocksUninstallAndDoesNotTouchRecord()
    {
        using var f=new Fixture();f.Admit();Directory.CreateDirectory(f.State);f.Pending=[new(f.State,false,false,true,"Unresolved")];
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.AssertClear(f.App,f.Context));Assert.Single(Directory.EnumerateFiles(f.Users));
    }
    [Fact] public void InaccessibleStateInspectionIsNeverTreatedAsAbsence()
    {
        using var f=new Fixture();f.Admit();Directory.CreateDirectory(f.State);f.InspectionFailure=new UnauthorizedAccessException("No access");
        Assert.Throws<UnauthorizedAccessException>(()=>InstallationRecoveryIndex.AssertClear(f.App,f.Context));
    }
    [Fact] public void ConfirmedMissingStateIsClearWithoutInventingRecoveryReceipts()
    {
        using var f=new Fixture();f.Admit();f.InspectionFailure=new Exception("Must not inspect absent state");
        InstallationRecoveryIndex.AssertClear(f.App,f.Context);Assert.False(Directory.Exists(f.State));
    }
    [Theory] [InlineData("unknown.txt")] [InlineData("S-1-0-0.pending.tmp")]
    public void UnknownIndexFilesFailClosed(string filename)
    {
        using var f=new Fixture();f.Admit();File.WriteAllText(Path.Combine(f.Users,filename),"preserve");
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.AssertClear(f.App,f.Context));Assert.Equal("preserve",File.ReadAllText(Path.Combine(f.Users,filename)));
    }
    [Fact] public void MalformedOrMismatchedRecordIsPreservedAndBlocksUninstall()
    {
        using var f=new Fixture();f.Admit();string path=Assert.Single(Directory.EnumerateFiles(f.Users));File.WriteAllText(path,"{}");
        Assert.ThrowsAny<Exception>(()=>InstallationRecoveryIndex.AssertClear(f.App,f.Context));Assert.Equal("{}",File.ReadAllText(path));
    }
    [Fact] public void RecordAclCannotGrantAnotherPrincipalWriteAccess()
    {
        using var f=new Fixture();f.Admit();string path=Assert.Single(Directory.EnumerateFiles(f.Users));var info=new FileInfo(path);var acl=info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.Write,AccessControlType.Allow));info.SetAccessControl(acl);
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.AssertClear(f.App,f.Context));Assert.True(File.Exists(path));
    }
    [Fact] public void MissingScopeCannotDowngradeAnExistingIndexToPortableAdmission()
    {
        using var f=new Fixture();f.Admit();File.Delete(Path.Combine(f.App,"maintenance-scope.txt"));
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.RegisterCurrentUser(f.App,f.State,f.Context));
    }
    [Fact] public void MissingScopeCannotDowngradeARegisteredInstallation()
    {
        using var f=new Fixture();var context=f.Context with{RegisteredInstallation=_=>true};
        Assert.Throws<IOException>(()=>InstallationRecoveryIndex.RegisterCurrentUser(f.App,f.State,context));
    }
    private sealed class Fixture:IDisposable
    {
        private readonly CursorFixture directory=new();
        public string App=>Path.Combine(directory.DirectoryPath,"app");public string Data=>Path.Combine(directory.DirectoryPath,"data");public string State=>Path.Combine(directory.DirectoryPath,"state");
        public IReadOnlyList<PendingRecovery> Pending=[];public Exception? InspectionFailure;
        public InstallationRecoveryIndex.IndexContext Context=>new(Data,Data,WindowsIdentity.GetCurrent().User!,false,_=>{},_=>InspectionFailure is null?Pending:throw InspectionFailure);
        public string Users=>InstallationRecoveryIndex.UsersDirectory(App,"user",Context);
        public Fixture(){Directory.CreateDirectory(App);Directory.CreateDirectory(Data);}
        public void Scope(string text)=>File.WriteAllText(Path.Combine(App,"maintenance-scope.txt"),text);
        public void Admit(){Scope("user");InstallationRecoveryIndex.Provision(App,Context);InstallationRecoveryIndex.RegisterCurrentUser(App,State,Context);}
        public void Dispose()=>directory.Dispose();
    }
}
