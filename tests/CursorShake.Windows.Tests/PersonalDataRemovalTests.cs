using CursorShake.Windows.Configuration;
using CursorShake.Windows.Lifecycle;
namespace CursorShake.Windows.Tests;

public sealed class PersonalDataRemovalTests
{
    [Fact] public void MissingDataIsANoOpAndDoesNotCreateDirectories()
    {
        using var f=new Fixture();var plan=f.Prepare();Assert.Empty(plan.Paths);plan.Delete();Assert.False(Directory.Exists(f.Workspace));Assert.Equal(1,f.Retired);
    }
    [Fact] public void OnlyConfigThemesAndEmptyInternalDirectoriesAreRetired()
    {
        using var f=new Fixture();f.Write("config.toml","user config");f.Write("themes/custom/cursor.cur","asset");f.Write("unrelated.txt","keep");Directory.CreateDirectory(Path.Combine(f.State,"recovery"));
        var plan=f.Prepare();Assert.Contains(Path.Combine(f.Workspace,"config.toml"),plan.Paths);plan.Delete();
        Assert.False(File.Exists(Path.Combine(f.Workspace,"config.toml")));Assert.False(Directory.Exists(Path.Combine(f.Workspace,"themes")));Assert.False(Directory.Exists(f.State));Assert.Equal("keep",File.ReadAllText(Path.Combine(f.Workspace,"unrelated.txt")));Assert.Equal(1,f.Retired);
    }
    [Fact] public void ChangedFilePreventsEveryDeletion()
    {
        using var f=new Fixture();f.Write("config.toml","original");f.Write("themes/a.cur","asset");var plan=f.Prepare();f.Write("themes/a.cur","changed");
        Assert.Throws<IOException>(plan.Delete);Assert.True(File.Exists(Path.Combine(f.Workspace,"config.toml")));Assert.Equal(0,f.Retired);
    }
    [Fact] public void AddedThemeFilePreventsEveryDeletion()
    {
        using var f=new Fixture();f.Write("config.toml","original");f.Write("themes/a.cur","asset");var plan=f.Prepare();f.Write("themes/new.cur","new");
        Assert.Throws<IOException>(plan.Delete);Assert.True(File.Exists(Path.Combine(f.Workspace,"config.toml")));Assert.Equal(0,f.Retired);
    }
    [Fact] public void ChangedAuthorityPreventsEveryDeletion()
    {
        using var f=new Fixture();f.Write("config.toml","original");f.Authority=new(f.Workspace);var plan=f.Prepare();f.Authority=new(f.Workspace,"move","CleanupPending");
        Assert.Throws<IOException>(plan.Delete);Assert.True(File.Exists(Path.Combine(f.Workspace,"config.toml")));Assert.Equal(0,f.Retired);
    }
    [Theory] [InlineData("x.write.json")] [InlineData("x.move.json")] [InlineData("unknown.txt")]
    public void UnknownOrUnfinishedInternalStateIsPreserved(string name)
    {
        using var f=new Fixture();Directory.CreateDirectory(f.State);File.WriteAllText(Path.Combine(f.State,name),"pending");f.Write("config.toml","original");
        Assert.Throws<IOException>(()=>f.Prepare());Assert.True(File.Exists(Path.Combine(f.Workspace,"config.toml")));Assert.Equal("pending",File.ReadAllText(Path.Combine(f.State,name)));
    }
    [Fact] public void AnyRecoveryContentBlocksRemoval()
    {
        using var f=new Fixture();string recovery=Path.Combine(f.State,"recovery",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(recovery);File.WriteAllText(Path.Combine(recovery,"result.json"),"preserve");
        Assert.Throws<IOException>(()=>f.Prepare());Assert.True(Directory.Exists(recovery));
    }
    [Fact] public void ContextIsRecheckedAtCommit()
    {
        using var f=new Fixture();f.Write("config.toml","original");var plan=f.Prepare();f.Elevated=true;
        Assert.Throws<IOException>(plan.Delete);Assert.True(File.Exists(Path.Combine(f.Workspace,"config.toml")));
    }
    [Fact] public void WriterOpenAtCommitStopsRemovalBeforeAnyDeletion()
    {
        using var f=new Fixture();f.Write("config.toml","original");f.Write("themes/a.cur","asset");var plan=f.Prepare();
        using var writer=new FileStream(Path.Combine(f.Workspace,"themes/a.cur"),FileMode.Open,FileAccess.Write,FileShare.ReadWrite);
        Assert.Throws<ConfigurationException>(plan.Delete);Assert.True(File.Exists(Path.Combine(f.Workspace,"config.toml")));
    }
    [Fact] public void HardLinkedThemeIsRejectedWithoutTouchingItsOtherName()
    {
        using var f=new Fixture();f.Write("outside.cur","precious");Directory.CreateDirectory(Path.Combine(f.Workspace,"themes"));
        string original=Path.Combine(f.Workspace,"outside.cur"),link=Path.Combine(f.Workspace,"themes","a.cur");
        Assert.True(CreateHardLink(link,original,0),"CreateHardLink failed: "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        Assert.Throws<ConfigurationException>(()=>f.Prepare());Assert.Equal("precious",File.ReadAllText(original));Assert.True(File.Exists(link));
    }
    [Fact] public void ReplacedFileWithIdenticalContentFailsIdentityPreflight()
    {
        using var f=new Fixture();f.Write("config.toml","original");var plan=f.Prepare();
        File.Move(Path.Combine(f.Workspace,"config.toml"),Path.Combine(f.Workspace,"preserved.toml"));f.Write("config.toml","original");
        Assert.Throws<IOException>(plan.Delete);Assert.True(File.Exists(Path.Combine(f.Workspace,"config.toml")));Assert.True(File.Exists(Path.Combine(f.Workspace,"preserved.toml")));Assert.Equal(0,f.Retired);
    }
    [Theory] [InlineData("CleanupPending")] [InlineData("Unknown")]
    public void NonReadyAuthorityPreventsPlanCreation(string phase)
    {
        using var f=new Fixture();f.Write("config.toml","original");f.Authority=new(f.Workspace,"operation",phase);
        Assert.Throws<IOException>(()=>f.Prepare());Assert.True(File.Exists(Path.Combine(f.Workspace,"config.toml")));
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll",EntryPoint="CreateHardLinkW",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
    [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newName,string existing,nint security);
    private sealed class Fixture:IDisposable
    {
        private readonly CursorFixture directory=new();
        public string Workspace=>Path.Combine(directory.DirectoryPath,"workspace");public string State=>Path.Combine(directory.DirectoryPath,"state");
        public WorkspaceLocator? Authority;public int Retired;public bool Elevated;
        public void Write(string relative,string text){string path=Path.Combine(Workspace,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text);}
        public PersonalDataRemovalPlan Prepare()=>PersonalDataRemoval.Prepare(Workspace,State,()=>Authority,expected=>{Assert.Equal(expected,Authority);Retired++;Authority=null;},()=>{if(Elevated)throw new IOException("Must be unelevated");});
        public void Dispose()=>directory.Dispose();
    }
}
