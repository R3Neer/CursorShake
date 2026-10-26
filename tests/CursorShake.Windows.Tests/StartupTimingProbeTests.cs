using System.Diagnostics;
using System.Security.Principal;
using CursorShake.Windows.Lifecycle;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class StartupTimingProbeTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
    private const string Executable = @"D:\Apps\CursorShake\CursorShake.exe";

    [Fact]
    public void RequestMatchesTheExactCanonicalExecutableIgnoringWindowsCase()
    {
        var request = Armed() with { Executable = @"d:\apps\CURSORSHAKE\.\CursorShake.exe" };
        Assert.True(StartupTimingProbe.IsArmedFor(request, Executable, Epoch));
        Assert.False(StartupTimingProbe.IsArmedFor(request, @"D:\Apps\OtherCopy\CursorShake.exe", Epoch));
        Assert.False(StartupTimingProbe.IsArmedFor(request, @"D:\Apps\CursorShake\CursorShake.RecoveryGuard.exe", Epoch));
    }

    [Fact]
    public void RelativeRequestExecutableCannotArmTheProbe()
    {
        Assert.False(StartupTimingProbe.IsArmedFor(Armed() with { Executable = "CursorShake.exe" }, Executable, Epoch));
    }

    [Fact]
    public void EmptyRequestIdentityCannotArmTheProbe()
    {
        Assert.False(StartupTimingProbe.IsArmedFor(Armed() with { Id = Guid.Empty }, Executable, Epoch));
    }

    [Fact]
    public void ArmingInstantIsInclusiveButExpiryInstantIsExclusive()
    {
        var request = Armed() with { ArmedUtc = Epoch, ExpiresUtc = Epoch.AddHours(1) };
        Assert.True(StartupTimingProbe.IsArmedFor(request, Executable, Epoch));
        Assert.True(StartupTimingProbe.IsArmedFor(request, Executable, request.ExpiresUtc.AddTicks(-1)));
        Assert.False(StartupTimingProbe.IsArmedFor(request, Executable, request.ExpiresUtc));
        Assert.False(StartupTimingProbe.IsArmedFor(request, Executable, request.ExpiresUtc.AddTicks(1)));
    }

    [Fact]
    public void FutureArmingAndExpiredOrReversedWindowsAreRejected()
    {
        Assert.False(StartupTimingProbe.IsArmedFor(Armed() with { ArmedUtc = Epoch.AddTicks(1) }, Executable, Epoch));
        Assert.False(StartupTimingProbe.IsArmedFor(Armed() with { ExpiresUtc = Epoch.AddTicks(-1) }, Executable, Epoch));
        Assert.False(StartupTimingProbe.IsArmedFor(Armed() with { ExpiresUtc = Epoch.AddDays(-2) }, Executable, Epoch));
    }

    [Fact]
    public void RequestLifetimeAllowsExactlySevenDaysButNotOneTickMore()
    {
        var request = Armed() with { ArmedUtc = Epoch.AddDays(-1), ExpiresUtc = Epoch.AddDays(6) };
        Assert.True(StartupTimingProbe.IsArmedFor(request, Executable, Epoch));
        Assert.False(StartupTimingProbe.IsArmedFor(request with { ExpiresUtc = request.ExpiresUtc.AddTicks(1) }, Executable, Epoch));
    }

    [Fact]
    public void EquivalentUtcInstantsAreUsedForRequestValidity()
    {
        var request = Armed() with { ArmedUtc = Epoch.ToOffset(TimeSpan.FromHours(2)), ExpiresUtc = Epoch.AddHours(1).ToOffset(TimeSpan.FromHours(-4)) };
        Assert.True(StartupTimingProbe.IsArmedFor(request, Executable, Epoch));
    }

    [Fact]
    public void MeasurementPreservesIdentityAndSeparatesLogonDelayFromInitialization()
    {
        var request = Armed(); var logon = Session(Epoch);
        var process = Epoch.AddMilliseconds(12_500); var operational = process.AddMilliseconds(2_750);
        var result = StartupTimingProbe.CreateMeasurement(request, logon, process, operational, 1234);
        Assert.Equal(request.Id, result.RequestId);
        Assert.Equal(request.Executable, result.Executable);
        Assert.Equal(1234, result.ProcessId);
        Assert.Same(logon, result.Session);
        Assert.Equal(process, result.ProcessStartUtc);
        Assert.Equal(operational, result.OperationalUtc);
        Assert.Equal(12_500, result.LogonToProcessMs);
        Assert.Equal(2_750, result.InitializationMs);
        Assert.Equal(15_250, result.TotalMs);
        Assert.Equal(result.TotalMs, result.LogonToProcessMs + result.InitializationMs);
        Assert.True(result.UnderOneMinute);
    }

    [Theory]
    [InlineData(59_999, true)]
    [InlineData(60_000, false)]
    [InlineData(60_001, false)]
    public void OneMinuteAcceptanceUsesAStrictLessThanBoundary(int totalMilliseconds, bool expected)
    {
        var result = StartupTimingProbe.CreateMeasurement(Armed(), Session(Epoch), Epoch.AddSeconds(1), Epoch.AddMilliseconds(totalMilliseconds), 42);
        Assert.Equal(totalMilliseconds, result.TotalMs);
        Assert.Equal(expected, result.UnderOneMinute);
    }

    [Fact]
    public void OneTickBelowTheMinutePassesAndOneTickAboveFails()
    {
        var before = StartupTimingProbe.CreateMeasurement(Armed(), Session(Epoch), Epoch, Epoch.AddMinutes(1).AddTicks(-1), 42);
        var after = StartupTimingProbe.CreateMeasurement(Armed(), Session(Epoch), Epoch, Epoch.AddMinutes(1).AddTicks(1), 42);
        Assert.True(before.UnderOneMinute);
        Assert.False(after.UnderOneMinute);
    }

    [Fact]
    public void IdenticalTimestampsAreOrderedAndHaveNoDelay()
    {
        var result = StartupTimingProbe.CreateMeasurement(Armed(), Session(Epoch), Epoch, Epoch, 42);
        Assert.Equal(0, result.LogonToProcessMs);
        Assert.Equal(0, result.InitializationMs);
        Assert.Equal(0, result.TotalMs);
        Assert.True(result.UnderOneMinute);
    }

    [Fact]
    public void ProcessBeforeLogonCannotBeCertified()
    {
        Assert.Throws<InvalidOperationException>(() => StartupTimingProbe.CreateMeasurement(Armed(), Session(Epoch), Epoch.AddTicks(-1), Epoch.AddSeconds(1), 42));
    }

    [Fact]
    public void OperationalBeforeProcessStartCannotBeCertified()
    {
        Assert.Throws<InvalidOperationException>(() => StartupTimingProbe.CreateMeasurement(Armed(), Session(Epoch), Epoch.AddSeconds(1), Epoch, 42));
    }

    [Fact]
    public void MeasurementDurationsUseAbsoluteInstantsAcrossDifferentUtcOffsets()
    {
        var logon = Session(Epoch.ToOffset(TimeSpan.FromHours(2)));
        var result = StartupTimingProbe.CreateMeasurement(Armed(), logon, Epoch.AddSeconds(3).ToOffset(TimeSpan.FromHours(-4)), Epoch.AddSeconds(8), 42);
        Assert.Equal(3000, result.LogonToProcessMs);
        Assert.Equal(5000, result.InitializationMs);
        Assert.Equal(8000, result.TotalMs);
    }

    [Fact]
    [Trait("Category", "NativeReadOnly")]
    public void NativeCurrentLogonBelongsToTheCurrentIdentityAndProcessSession()
    {
        using var process = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        var before = DateTimeOffset.UtcNow;
        var logon = StartupTimingProbe.ReadCurrentLogon();
        var again = StartupTimingProbe.ReadCurrentLogon();
        Assert.Equal(identity.User!.Value, logon.Sid);
        Assert.Equal(process.SessionId, logon.SessionId);
        Assert.Matches("^[0-9a-f]{16}$", logon.AuthenticationId);
        Assert.True(logon.LogonType > 0);
        Assert.True(logon.LogonUtc > DateTimeOffset.FromUnixTimeSeconds(0));
        Assert.True(logon.LogonUtc <= before);
        Assert.True(logon.LogonUtc <= new DateTimeOffset(process.StartTime.ToUniversalTime()));
        Assert.Equal(logon, again);
    }

    private static StartupTimingProbe.Request Armed() => new(Guid.NewGuid(), Executable, Epoch.AddHours(-1), Epoch.AddHours(1));
    private static StartupTimingProbe.Logon Session(DateTimeOffset when) => new("000000000000abcd", "S-1-5-21-100-200-300-1001", 7, 2, when);
}
