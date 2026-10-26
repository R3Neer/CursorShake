using System.ComponentModel;
using System.Runtime.InteropServices;
using CursorShake.Core.Input;
namespace CursorShake.Windows.Input;

public enum RawPointerKind { Move,LeftDown,LeftUp,MiddleDown,MiddleUp,RightDown,RightUp }
public sealed record RawPointerEvent(RawPointerKind Kind,int X,int Y);

/// <summary>Dedicated hook/message thread. Callback only copies the sample into the serial admission queue.</summary>
public sealed class WindowsPointerSource : IDisposable
{
    private readonly Action<RawPointerEvent> received;
    private readonly Thread thread;
    private readonly Hook callback;
    private readonly ManualResetEventSlim ready=new();
    private uint threadId;
    private nint hook;
    private Exception? startupError;
    private bool disposed;
    private int observedButtons;
    public PointerButtons CurrentButtons=>(PointerButtons)Volatile.Read(ref observedButtons);
    private delegate nint Hook(int code,nint message,nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct HookData { public Point Point; public uint MouseData,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Value; public nuint W; public nint L; public uint Time; public Point Point; public uint Private; }
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetWindowsHookExW(int id,Hook callback,nint module,uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern int GetMessageW(out Message message,nint window,uint min,uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message,nint window,uint min,uint max,uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint thread,uint message,nuint w,nint l);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    public WindowsPointerSource(Action<RawPointerEvent> received)
    {
        this.received=received;callback=OnHook;thread=new Thread(Run){Name="CursorShake input",IsBackground=true};thread.Start();
        if(!ready.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("Mouse hook initialization timed out.");
        if(startupError!=null)throw new InvalidOperationException("Unable to observe mouse input.",startupError);
    }
    public static PointerButtons ReadButtons()
    {
        PointerButtons result=PointerButtons.None;
        if((GetAsyncKeyState(1)&0x8000)!=0)result|=PointerButtons.Left;
        if((GetAsyncKeyState(4)&0x8000)!=0)result|=PointerButtons.Middle;
        if((GetAsyncKeyState(2)&0x8000)!=0)result|=PointerButtons.Right;
        return result;
    }
    private void Run()
    {
        threadId=GetCurrentThreadId();PeekMessageW(out _,0,0,0,0);
        try
        {
            observedButtons=(int)ReadButtons();
            hook=SetWindowsHookExW(14,callback,GetModuleHandleW(null),0);
            if(hook==0)throw new Win32Exception(Marshal.GetLastWin32Error());ready.Set();
            while(GetMessageW(out _,0,0,0)>0){}
        }
        catch(Exception ex){startupError=ex;ready.Set();}
        finally{if(hook!=0)UnhookWindowsHookEx(hook);hook=0;}
    }
    private nint OnHook(int code,nint message,nint pointer)
    {
        if(code>=0)
        {
            var data=Marshal.PtrToStructure<HookData>(pointer);
            if((data.Flags&3)==0)
            {
                RawPointerKind? kind=(int)message switch{0x200=>RawPointerKind.Move,0x201=>RawPointerKind.LeftDown,0x202=>RawPointerKind.LeftUp,0x204=>RawPointerKind.RightDown,0x205=>RawPointerKind.RightUp,0x207=>RawPointerKind.MiddleDown,0x208=>RawPointerKind.MiddleUp,_=>null};
                if(kind is{} value){
                    var button=value switch {RawPointerKind.LeftDown or RawPointerKind.LeftUp=>PointerButtons.Left,RawPointerKind.MiddleDown or RawPointerKind.MiddleUp=>PointerButtons.Middle,RawPointerKind.RightDown or RawPointerKind.RightUp=>PointerButtons.Right,_=>PointerButtons.None};
                    bool down=value is RawPointerKind.LeftDown or RawPointerKind.MiddleDown or RawPointerKind.RightDown;
                    if(button!=PointerButtons.None)Volatile.Write(ref observedButtons,down?observedButtons|(int)button:observedButtons&~(int)button);
                    try{received(new(value,data.Point.X,data.Point.Y));}catch{/* Never throw through a native callback. */}}
            }
        }
        return CallNextHookEx(hook,code,message,pointer);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(threadId!=0)PostThreadMessageW(threadId,0x12,0,0);
        if(thread.IsAlive&&!thread.Join(TimeSpan.FromSeconds(5)))throw new TimeoutException("Input thread did not stop.");
        ready.Dispose();
    }
}
