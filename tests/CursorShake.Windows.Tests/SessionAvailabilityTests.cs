using CursorShake.Windows.Lifecycle;
using Microsoft.Win32;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class SessionAvailabilityTests
{
    [Fact]
    public void ResumeDoesNotClearAnIndependentSessionLock()
    {
        var state = new SessionAvailability(7);
        state.OnSessionSwitch(SessionSwitchReason.SessionLock, 7);
        state.OnPowerModeChanged(PowerModes.Suspend);
        state.OnPowerModeChanged(PowerModes.Resume);
        Assert.True(state.IsUnavailable);
        state.OnSessionSwitch(SessionSwitchReason.SessionUnlock, 7);
        Assert.False(state.IsUnavailable);
    }

    [Fact]
    public void UnlockDoesNotClearAnIndependentSuspend()
    {
        var state = new SessionAvailability(7);
        state.OnSessionSwitch(SessionSwitchReason.SessionLock, 7);
        state.OnPowerModeChanged(PowerModes.Suspend);
        state.OnSessionSwitch(SessionSwitchReason.SessionUnlock, 7);
        Assert.True(state.IsUnavailable);
        state.OnPowerModeChanged(PowerModes.Resume);
        Assert.False(state.IsUnavailable);
    }

    [Fact]
    public void PowerStatusChangeDoesNotResumeASuspendedSession()
    {
        var state = new SessionAvailability(7);
        state.OnPowerModeChanged(PowerModes.Suspend);
        state.OnPowerModeChanged(PowerModes.StatusChange);
        Assert.True(state.IsUnavailable);
        state.OnPowerModeChanged(PowerModes.Resume);
        Assert.False(state.IsUnavailable);
    }

    [Fact]
    public void OtherSessionsCannotLockOrUnlockThisSession()
    {
        var state = new SessionAvailability(7);
        state.OnSessionSwitch(SessionSwitchReason.SessionLock, 8);
        Assert.False(state.IsUnavailable);
        state.OnSessionSwitch(SessionSwitchReason.SessionLock, 7);
        state.OnSessionSwitch(SessionSwitchReason.SessionUnlock, 8);
        Assert.True(state.IsUnavailable);
    }

    [Fact]
    public void ConnectionChangesDoNotClearIndependentLockOrLogoffReasons()
    {
        var state = new SessionAvailability(7);
        state.OnSessionSwitch(SessionSwitchReason.SessionLock, 7);
        state.OnSessionSwitch(SessionSwitchReason.SessionLogoff, 7);
        state.OnSessionSwitch(SessionSwitchReason.ConsoleDisconnect, 7);
        state.OnSessionSwitch(SessionSwitchReason.ConsoleConnect, 7);
        Assert.True(state.IsUnavailable);
        state.OnSessionSwitch(SessionSwitchReason.SessionUnlock, 7);
        Assert.True(state.IsUnavailable);
        state.OnSessionSwitch(SessionSwitchReason.SessionLogon, 7);
        Assert.False(state.IsUnavailable);
    }

    [Fact]
    public void UnlockAloneCannotReconnectADisconnectedSession()
    {
        var state = new SessionAvailability(7);
        state.OnSessionSwitch(SessionSwitchReason.RemoteDisconnect, 7);
        state.OnSessionSwitch(SessionSwitchReason.SessionUnlock, 7);
        Assert.True(state.IsUnavailable);
        state.OnSessionSwitch(SessionSwitchReason.RemoteConnect, 7);
        Assert.False(state.IsUnavailable);
    }
}
