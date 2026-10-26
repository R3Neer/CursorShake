using CursorShake.Core.Input;
using CursorShake.Core.Shake;
using CursorShake.Windows.Cursors.Native;
namespace CursorShake.Windows.Input;

public sealed class PointerInput : IPointerInput
{
    private readonly ShakeStrokeRecognizer recognizer;
    private readonly Func<PointerButtons> readButtons;
    private readonly Func<int,int,(nint Monitor,double Dpi)> contextProvider;
    private (int X,int Y)? previous;
    private nint monitor;
    private double x,y,dpi=96;
    public PointerInput(GestureProfile profile,Func<PointerButtons>? readButtons=null,Func<int,int,(nint Monitor,double Dpi)>? contextProvider=null)
    {recognizer=new(profile);this.readButtons=readButtons??WindowsPointerSource.ReadButtons;this.contextProvider=contextProvider??ReadContext;CurrentButtons=this.readButtons();}
    public event Action<ShakeStroke>? Stroke;
    public event Action<ButtonsChanged>? ButtonsChanged;
    public event Action<InputDiscontinuity>? Discontinuity;
    public PointerButtons CurrentButtons { get; private set; }
    public void ApplyProfile(GestureProfile profile){recognizer.ApplyProfile(profile);Reset();}
    public void Reset(){recognizer.Reset();previous=null;monitor=0;x=y=0;}
    public void SignalDiscontinuity(TimeSpan occurredAt)
    {Reset();CurrentButtons=readButtons();Discontinuity?.Invoke(new(occurredAt,CurrentButtons));}
    public void Process(RawPointerEvent raw,TimeSpan occurredAt)
    {
        if(raw.Kind!=RawPointerKind.Move)
        {
            (PointerButtons button,bool down)=raw.Kind switch
            {
                RawPointerKind.LeftDown=>(PointerButtons.Left,true),RawPointerKind.LeftUp=>(PointerButtons.Left,false),
                RawPointerKind.MiddleDown=>(PointerButtons.Middle,true),RawPointerKind.MiddleUp=>(PointerButtons.Middle,false),
                RawPointerKind.RightDown=>(PointerButtons.Right,true),RawPointerKind.RightUp=>(PointerButtons.Right,false),_=>throw new ArgumentOutOfRangeException(nameof(raw))
            };
            recognizer.Reset();previous=null;
            CurrentButtons=down?CurrentButtons|button:CurrentButtons&~button;
            ButtonsChanged?.Invoke(new(occurredAt,CurrentButtons));return;
        }
        var context=contextProvider(raw.X,raw.Y);
        if(context.Monitor!=monitor||context.Dpi!=dpi)
        {
            bool hadMonitor=monitor!=0;monitor=context.Monitor;
            if(!double.IsFinite(context.Dpi)||context.Dpi<=0)throw new InvalidOperationException("Invalid pointer DPI context.");
            dpi=context.Dpi;recognizer.Reset();previous=null;x=y=0;
            if(hadMonitor)Discontinuity?.Invoke(new(occurredAt,CurrentButtons));
        }
        if(previous is{} p){x+=(raw.X-p.X)*96.0/dpi;y+=(raw.Y-p.Y)*96.0/dpi;}
        previous=(raw.X,raw.Y);
        if(CurrentButtons!=PointerButtons.None)return;
        var stroke=recognizer.Add(new(x,y),occurredAt);
        if(stroke is{} confirmed)Stroke?.Invoke(confirmed);
    }
    private (nint Monitor,double Dpi) ReadContext(int px,int py)
    {
        var current=CursorNative.MonitorFromPoint(new(){X=px,Y=py},2);
        if(current==monitor)return(current,dpi);
        uint value=CursorNative.GetDpiForSystem();
        try{if(CursorNative.GetDpiForMonitor(current,0,out var next,out _)==0)value=next;}catch(DllNotFoundException){}
        return(current,value==0?96:value);
    }

}
