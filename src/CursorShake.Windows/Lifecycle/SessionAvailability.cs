using Microsoft.Win32;
namespace CursorShake.Windows.Lifecycle;

/// <summary>Independent Windows lifetime reasons; clearing one reason cannot clear another.</summary>
public sealed class SessionAvailability(int sessionId)
{
    private volatile bool locked,disconnected,suspended,loggedOff;
    public bool IsUnavailable=>locked||disconnected||suspended||loggedOff;
    public void OnSessionSwitch(SessionSwitchReason reason,int eventSessionId)
    {
        if(eventSessionId!=sessionId)return;
        switch(reason)
        {
            case SessionSwitchReason.SessionLock:locked=true;break;
            case SessionSwitchReason.SessionUnlock:locked=false;break;
            case SessionSwitchReason.ConsoleDisconnect:case SessionSwitchReason.RemoteDisconnect:disconnected=true;break;
            case SessionSwitchReason.ConsoleConnect:case SessionSwitchReason.RemoteConnect:disconnected=false;break;
            case SessionSwitchReason.SessionLogoff:loggedOff=true;break;
            case SessionSwitchReason.SessionLogon:loggedOff=false;break;
        }
    }
    public void OnPowerModeChanged(PowerModes mode)
    {
        if(mode==PowerModes.Suspend)suspended=true;
        else if(mode==PowerModes.Resume)suspended=false;
    }
}
