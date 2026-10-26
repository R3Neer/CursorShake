using CursorShake.App;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class AppInstallerMaintenanceTests
{
    private const string Root = @"D:\Installed\CursorShake";

    [Theory]
    [InlineData("--installer-quiesce", nameof(MaintenanceAction.Quiesce))]
    [InlineData("--installer-provision-index", nameof(MaintenanceAction.ProvisionIndex))]
    [InlineData("--installer-uninstall-keep-data", nameof(MaintenanceAction.UninstallKeepData))]
    [InlineData("--installer-uninstall-remove-data", nameof(MaintenanceAction.UninstallRemoveData))]
    public void OnlyTheExactTwoArgumentMaintenanceModesAreRecognized(string command, string expected)
    {
        var request = InstallerMaintenance.Parse([command, Root]);
        Assert.NotNull(request);
        Assert.Equal(expected, request.Action.ToString());
        Assert.Equal(Root, request.InstallationRoot);
        Assert.Equal(InstallerAction.Invalid, InstallerIntegration.Parse([command, Root]));
    }

    [Fact]
    public void FullyQualifiedPathsAreNormalizedWithoutTouchingTheirDirectories()
    {
        var request = InstallerMaintenance.Parse(["--installer-quiesce", @"D:\Installed\Intermediate\..\CursorShake\"]);
        Assert.NotNull(request);
        Assert.Equal(Root, request.InstallationRoot);
    }

    [Fact]
    public void EmptyArgumentsKeepTheNormalApplicationEntryPoint()
    {
        Assert.Null(InstallerMaintenance.Parse([]));
        Assert.Equal(InstallerAction.NormalLaunch, InstallerIntegration.Parse([]));
    }

    public static IEnumerable<object[]> InvalidMaintenanceInvocations()
    {
        yield return [new[] { "--installer-quiesce" }];
        yield return [new[] { "--installer-uninstall-keep-data" }];
        yield return [new[] { "--installer-uninstall-remove-data" }];
        yield return [new[] { "--installer-quiesce", Root, "extra" }];
        yield return [new[] { "--installer-uninstall-remove-data", Root, "--installer-launch" }];
        yield return [new[] { "--installer-uninstall", Root }];
        yield return [new[] { "--INSTALLER-QUIESCE", Root }];
        yield return [new[] { " --installer-quiesce", Root }];
        yield return [new[] { "--installer-quiesce ", Root }];
        yield return [new[] { "--installer-uninstall-remove-data=true", Root }];
        yield return [new[] { "--installer-launch", Root }];
        yield return [new[] { "--installer-startup=enable", Root }];
    }

    [Theory]
    [MemberData(nameof(InvalidMaintenanceInvocations))]
    public void InvalidArityOrCommandNeverFallsThroughToStartingTheApplication(string[] arguments)
    {
        Assert.Null(InstallerMaintenance.Parse(arguments));
        Assert.Equal(InstallerAction.Invalid, InstallerIntegration.Parse(arguments));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("relative/installation")]
    [InlineData(@"D:drive-relative")]
    [InlineData(@"\rooted-but-no-volume")]
    [InlineData("D:")]
    [InlineData("file:///D:/Installed/CursorShake")]
    [InlineData("\"D:\\Installed\\CursorShake\"")]
    public void APathWithoutAnUnambiguousFullyQualifiedWindowsRootIsRejected(string path)
    {
        string[] arguments = ["--installer-quiesce", path];
        Assert.Null(InstallerMaintenance.Parse(arguments));
        Assert.Equal(InstallerAction.Invalid, InstallerIntegration.Parse(arguments));
    }
}
