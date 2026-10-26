using System.Security.Principal;
using System.Xml;
using CursorShake.Windows.Lifecycle;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class InstalledStartupTasksTests
{
    private const string Root = @"D:\Installed\CursorShake";
    private const string Executable = Root + @"\CursorShake.exe";
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string OtherSid = "S-1-5-21-100-200-300-1002";

    [Fact]
    public void PlanSelectsOnlyOwnedTasksForTheExactInstallationIncludingOtherVisibleUsers()
    {
        var own = Owned(); var otherUser = Owned(OtherSid);
        var foreignCopy = Owned("S-1-5-21-100-200-300-1003") with { Actions = [new(true, @"D:\Other Copy\CursorShake.exe", "")] };
        var manuallyNamed = own with { Name = "User backup task" };
        var unrelated = own with { Name = "Foreign software", Source = "Foreign" };
        var plan = InstalledStartupTasks.Plan(Root, [own, otherUser, foreignCopy, manuallyNamed, unrelated]);
        Assert.Equal(new[] { own.Name, otherUser.Name }, plan.Select(item => item.Name));
    }

    [Fact]
    public void ActualAccountAliasesAreNormalizedToTheSidInTheTaskName()
    {
        using var user = WindowsIdentity.GetCurrent();
        var task = Owned(user.User!.Value) with { Principal = user.Name };
        Assert.Single(InstalledStartupTasks.Plan(Root, [task]));
    }

    [Fact]
    public void CanonicalPathComparisonAllowsCaseAndDotComponentsButNotASiblingInstallation()
    {
        var task = Owned() with { Actions = [new(true, @"d:\installed\CURSORSHAKE\.\CursorShake.exe", "")] };
        Assert.Single(InstalledStartupTasks.Plan(Root + "\\", [task]));
        Assert.Empty(InstalledStartupTasks.Plan(Root, [Owned() with { Actions = [new(true, @"D:\Installed\CursorShake Other\CursorShake.exe", "")] }]));
    }

    [Theory]
    [InlineData("Foreign")]
    [InlineData("cursorshake")]
    [InlineData(null)]
    public void ASourceConflictAtThisInstallationIsPreservedAndReported(string? source)
    {
        var error = Assert.Throws<InvalidOperationException>(() => InstalledStartupTasks.Plan(Root, [Owned() with { Source = source }]));
        Assert.Contains("Source", error.Message);
        Assert.Contains(Owned().Name, error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OtherSid)]
    public void UnknownOrDifferentPrincipalIsNeverAccepted(string? principal)
    {
        Assert.Throws<InvalidOperationException>(() => InstalledStartupTasks.Plan(Root, [Owned() with { Principal = principal }]));
    }

    [Fact]
    public void NamespaceWithAnInvalidSidDoesNotBecomeAnOwnedRegistration()
    {
        Assert.Throws<InvalidOperationException>(() => InstalledStartupTasks.Plan(Root, [Owned() with { Name = "CursorShake-invalid-sid" }]));
    }

    public static IEnumerable<object[]> UnsafeActions()
    {
        yield return [Array.Empty<InstalledStartupTaskAction>()];
        yield return [new[] { new InstalledStartupTaskAction(false, null, null) }];
        yield return [new[] { new InstalledStartupTaskAction(true, "CursorShake.exe", "") }];
        yield return [new[] { new InstalledStartupTaskAction(true, Executable, "--installer-launch") }];
        yield return [new[] { new InstalledStartupTaskAction(true, Executable, " ") }];
        yield return [new[] { new InstalledStartupTaskAction(true, Executable, ""), new InstalledStartupTaskAction(true, @"D:\Other\tool.exe", "") }];
    }

    [Theory]
    [MemberData(nameof(UnsafeActions))]
    public void OpaqueRelativeMultipleOrArgumentBearingActionsAbortBeforeAnyDelete(object actionObjects)
    {
        var actions = (InstalledStartupTaskAction[])actionObjects;
        var store = new FakeStore([Owned(OtherSid), Owned() with { Actions = actions }]);
        Assert.Throws<InvalidOperationException>(() => InstalledStartupTasks.RemoveForInstallation(Root, store));
        Assert.Empty(store.Deleted);
        Assert.Empty(store.ReadNames); // Validate the complete plan before the first mutation.
    }

    [Fact]
    public void ADefinitionChangedAfterPlanningIsRevalidatedAndPreserved()
    {
        var task = Owned(); var store = new FakeStore([task]) { OnRead = _ => task with { DefinitionXml = "changed settings or triggers" } };
        var error = Assert.Throws<InvalidOperationException>(() => InstalledStartupTasks.RemoveForInstallation(Root, store));
        Assert.Contains("changed", error.Message);
        Assert.Empty(store.Deleted);
        Assert.Equal(new[] { task.Name }, store.ReadNames);
    }

    [Fact]
    public void AReplacementRedirectedToAnotherCopyIsNotDeleted()
    {
        var task = Owned(); var store = new FakeStore([task]) { OnRead = _ => task with { Actions = [new(true, @"D:\Other\CursorShake.exe", "")] } };
        Assert.Throws<InvalidOperationException>(() => InstalledStartupTasks.RemoveForInstallation(Root, store));
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public void AlreadyRemovedTaskIsAnIdempotentNoOp()
    {
        var store = new FakeStore([Owned()]) { OnRead = _ => null };
        InstalledStartupTasks.RemoveForInstallation(Root, store);
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public void DeletionUsesOnlyNamesThatWereReadAgainAndVerified()
    {
        var own = Owned(); var other = Owned(OtherSid); var store = new FakeStore([own, other]);
        InstalledStartupTasks.RemoveForInstallation(Root, store);
        Assert.Equal(new[] { own.Name, other.Name }, store.ReadNames);
        Assert.Equal(store.ReadNames, store.Deleted);
    }

    [Fact]
    public void DeletionFailurePropagatesToTheInstallerInsteadOfReportingSuccess()
    {
        var expected = new UnauthorizedAccessException("Injected scheduler access denial");
        var store = new FakeStore([Owned()]) { DeleteFailure = expected };
        Assert.Same(expected, Assert.Throws<UnauthorizedAccessException>(() => InstalledStartupTasks.RemoveForInstallation(Root, store)));
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public void DuplicateCaseInsensitiveTaskNamesAreRejectedBeforeDeletion()
    {
        var task = Owned(); var store = new FakeStore([task, task with { Name = task.Name.ToUpperInvariant() }]);
        Assert.Throws<InvalidOperationException>(() => InstalledStartupTasks.RemoveForInstallation(Root, store));
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public void RelativeInstallationRootIsRejectedWithoutReadingTheStore()
    {
        var store = new FakeStore([Owned()]);
        Assert.Throws<ArgumentException>(() => InstalledStartupTasks.RemoveForInstallation("relative-installation", store));
        Assert.Equal(0, store.ReadAllCalls);
        Assert.Empty(store.ReadNames);
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public void RealTaskXmlCanBePlannedWithoutComOrSchedulerMutations()
    {
        string xml = $"""<Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><RegistrationInfo><Source>CursorShake</Source></RegistrationInfo><Principals><Principal id="Author"><UserId>{Sid}</UserId></Principal></Principals><Actions Context="Author"><Exec><Command>{Executable}</Command></Exec></Actions></Task>""";
        var task = InstalledStartupTasks.ParseDefinition("CursorShake-" + Sid, xml);
        Assert.Single(InstalledStartupTasks.Plan(Root, [task]));
        Assert.Equal(xml, task.DefinitionXml);
        Assert.True(task.Actions[0].IsExec);
    }

    [Fact]
    public void TaskXmlNeverResolvesExternalEntities()
    {
        Assert.Throws<XmlException>(() => InstalledStartupTasks.ParseDefinition("CursorShake-" + Sid, "<!DOCTYPE Task [<!ENTITY x SYSTEM 'file:///does-not-exist'>]><Task xmlns='http://schemas.microsoft.com/windows/2004/02/mit/task'>&x;</Task>"));
    }

    private static InstalledStartupTask Owned(string sid = Sid) => new("CursorShake-" + sid, "CursorShake", sid, [new(true, Executable, "")]);
    private sealed class FakeStore(IReadOnlyList<InstalledStartupTask> tasks) : IInstalledStartupTaskStore
    {
        public Func<string, InstalledStartupTask?>? OnRead { get; init; }
        public Exception? DeleteFailure { get; init; }
        public List<string> ReadNames { get; } = [];
        public List<string> Deleted { get; } = [];
        public int ReadAllCalls { get; private set; }
        public IReadOnlyList<InstalledStartupTask> ReadAll() { ReadAllCalls++; return tasks; }
        public InstalledStartupTask? Read(string name) { ReadNames.Add(name); return OnRead is null ? tasks.Single(task => task.Name == name) : OnRead(name); }
        public void Delete(string name) { if (DeleteFailure is not null) throw DeleteFailure; Deleted.Add(name); }
    }
}
