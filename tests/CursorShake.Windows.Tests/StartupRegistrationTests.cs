using CursorShake.Windows.Lifecycle;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class StartupRegistrationTests
{
    [Fact]
    [Trait("Category", "NativeReadOnly")]
    public void MissingStartupTaskIsReportedAsDisabledWithoutCreatingIt()
    {
        string task = "CursorShake.Tests.Absent." + Guid.NewGuid().ToString("N");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host executable is unavailable.");
        var registration = new StartupRegistration(executable, task);
        Assert.False(registration.IsEnabled());
        Assert.False(registration.IsEnabled()); // A read cannot register the missing task as a side effect.
    }
}
