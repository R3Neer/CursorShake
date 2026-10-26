using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace CursorShake.Windows.Lifecycle;

/// <summary>WTS delivers the session identity omitted by SystemEvents.SessionSwitch.</summary>
public sealed class SessionNotificationSource : NativeWindow,IDisposable
{
    private readonly Action<SessionSwitchReason,int> received;
    private bool registered;
    [DllImport("wtsapi32.dll",SetLastError=true)] private static extern bool WTSRegisterSessionNotification(nint window,uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint window);
    public SessionNotificationSource(Action<SessionSwitchReason,int> received)
    {
        this.received=received;CreateHandle(new CreateParams{Caption="CursorShake session notifications"});
        if(!WTSRegisterSessionNotification(Handle,0)){var error=new Win32Exception(Marshal.GetLastWin32Error());DestroyHandle();throw error;}
        registered=true;
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x02B1&&Enum.IsDefined(typeof(SessionSwitchReason),(int)message.WParam))
            received((SessionSwitchReason)(int)message.WParam,(int)message.LParam);
        base.WndProc(ref message);
    }
    public void Dispose(){if(registered){WTSUnRegisterSessionNotification(Handle);registered=false;}DestroyHandle();}
}
