using System.Runtime.InteropServices;
namespace CursorShake.Windows.Cursors;

/// <summary>Install on the application's message-loop thread; observes cursor events without intercepting input.</summary>
public sealed class CursorObservationSource : IDisposable
{
    private readonly WinEvent callback;
    private nint hook;
    private readonly Action changed;
    private delegate void WinEvent(nint hook,uint evt,nint window,int objectId,int child,uint thread,uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min,uint max,nint module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    public CursorObservationSource(Action changed)
    {
        this.changed=changed;callback=OnEvent;hook=SetWinEventHook(0x8000,0x800c,0,callback,0,0,0);
        if(hook==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Observe cursor events");
    }
    private void OnEvent(nint h,uint evt,nint window,int objectId,int child,uint thread,uint time)
    {if(objectId==-9&&child==0)changed();}
    public void Dispose(){if(hook!=0){UnhookWinEvent(hook);hook=0;}}
}
