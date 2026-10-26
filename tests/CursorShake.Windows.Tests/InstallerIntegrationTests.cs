using CursorShake.App;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class InstallerIntegrationTests
{
    public static IEnumerable<object[]> AcceptedInvocations()
    {
        yield return [Array.Empty<string>(), nameof(InstallerAction.NormalLaunch)];
        yield return [new[] { "--installer-launch" }, nameof(InstallerAction.InstallerLaunch)];
        yield return [new[] { "--installer-startup=enable" }, nameof(InstallerAction.EnableStartup)];
        yield return [new[] { "--installer-startup=disable" }, nameof(InstallerAction.DisableStartup)];
    }

    [Theory]
    [MemberData(nameof(AcceptedInvocations))]
    public void ExactPrivateInstallerInvocationsAreRecognized(string[] arguments, string expected) => Assert.Equal(expected, InstallerIntegration.Parse(arguments).ToString());

    public static IEnumerable<object[]> InvalidInvocations()
    {
        yield return [new[] { "" }];
        yield return [new[] { "--help" }];
        yield return [new[] { "--installer-startup" }];
        yield return [new[] { "--installer-startup=" }];
        yield return [new[] { "--installer-startup=true" }];
        yield return [new[] { "--installer-startup=ENABLE" }];
        yield return [new[] { "--INSTALLER-LAUNCH" }];
        yield return [new[] { " --installer-launch" }];
        yield return [new[] { "--installer-launch " }];
        yield return [new[] { "--installer-launch", "extra" }];
        yield return [new[] { "--installer-startup=enable", "--installer-launch" }];
        yield return [new[] { "--installer-startup=disable", "--installer-startup=enable" }];
    }

    [Theory]
    [MemberData(nameof(InvalidInvocations))]
    public void UnknownPartialOrExtraArgumentsNeverBecomeANormalLaunch(string[] arguments)
    {
        var action = InstallerIntegration.Parse(arguments);
        Assert.Equal(InstallerAction.Invalid, action);
        int calls = 0;
        var result = InstallerIntegration.Execute(action, true, false, _ => calls++);
        Assert.False(result.StartApplication);
        Assert.Equal(2, result.ExitCode);
        Assert.Null(result.Error);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NormalLaunchKeepsItsExistingContextBehaviorAndDoesNotTouchStartup(bool interactive, bool elevated)
    {
        int calls = 0;
        var result = InstallerIntegration.Execute(InstallerAction.NormalLaunch, interactive, elevated, _ => calls++);
        Assert.True(result.StartApplication);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void InteractiveUnelevatedInstallerLaunchRequestsApplicationStartupWithoutConfiguringTheTask()
    {
        int calls = 0;
        var result = InstallerIntegration.Execute(InstallerAction.InstallerLaunch, true, false, _ => calls++);
        Assert.True(result.StartApplication);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Error);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UnsafeInstallerContextCannotLaunchTheApplicationOrChangeStartupIncludingDisabling(bool interactive, bool elevated)
    {
        foreach (var action in new[] { InstallerAction.InstallerLaunch, InstallerAction.EnableStartup, InstallerAction.DisableStartup })
        {
            int calls = 0;
            var result = InstallerIntegration.Execute(action, interactive, elevated, _ => calls++);
            Assert.False(result.StartApplication);
            Assert.Equal(3, result.ExitCode);
            Assert.IsType<InvalidOperationException>(result.Error);
            Assert.Equal(0, calls);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartupMaintenanceConfiguresExactlyOnceAndNeverRequestsAnApplicationHost(bool enabled)
    {
        var calls = new List<bool>();
        var action = enabled ? InstallerAction.EnableStartup : InstallerAction.DisableStartup;
        var result = InstallerIntegration.Execute(action, true, false, calls.Add);
        Assert.Equal(new[] { enabled }, calls);
        Assert.False(result.StartApplication);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedMaintenancePreservesTheOriginalExceptionAndNeverFallsThroughToApplicationStartup(bool enabled)
    {
        var expected = new IOException("Injected startup registration failure");
        int calls = 0;
        var result = InstallerIntegration.Execute(enabled ? InstallerAction.EnableStartup : InstallerAction.DisableStartup, true, false, _ => { calls++; throw expected; });
        Assert.False(result.StartApplication);
        Assert.Equal(1, result.ExitCode);
        Assert.Same(expected, result.Error);
        Assert.Equal(1, calls);
    }
}
