using CursorShake.Core.Runtime;
using CursorShake.Core.Shake;
namespace CursorShake.Windows.Input;

/// <summary>Serial ownership of domain/configuration commands with bounded input admission and deadline wakeups.</summary>
public sealed class PointerRuntimeLoop : IDisposable
{
    private sealed record Item(TimeSpan At,RawPointerEvent? Pointer,Action? Action,bool Deadline=false,bool Discontinuity=false);
    private readonly object gate=new();
    private readonly Queue<Item> queue=new();
    private readonly AutoResetEvent wake=new(false);
    private readonly Thread thread;
    private readonly IMonotonicClock clock;
    private readonly PointerInput input;
    private readonly ShakeDetector detector;
    private bool stopping,overflow,deadlineQueued;
    private TimeSpan watermark;
    public static TimeSpan MaximumInputAge { get; }=TimeSpan.FromMilliseconds(100);
    public PointerRuntimeLoop(IMonotonicClock clock,PointerInput input,ShakeDetector detector)
    {
        this.clock=clock;this.input=input;this.detector=detector;
        thread=new Thread(Run){Name="CursorShake domain",IsBackground=true};thread.Start();
    }
    internal Action<TimeSpan>? BeforeDeadline {get;set;}
    public event Action<Exception>? Error;
    public void Accept(RawPointerEvent value)
    {
        lock(gate)
        {
            if(stopping)return;
            if(queue.Count>=2048||overflow)
            {
                if(!overflow){overflow=true;queue.Enqueue(new(clock.Now,null,null,Discontinuity:true));}
                wake.Set();return;
            }
            queue.Enqueue(new(clock.Now,value,null));
        }
        wake.Set();
    }
    public Task InvokeAsync(Action action)
    {
        var result=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(gate)
        {
            if(stopping)return Task.FromException(new ObjectDisposedException(nameof(PointerRuntimeLoop)));
            queue.Enqueue(new(clock.Now,null,()=>{try{action();result.SetResult();}catch(Exception ex){result.SetException(ex);}}));
        }
        wake.Set();return result.Task;
    }
    private void Run()
    {
        while(true)
        {
            Item? item=null;TimeSpan? due=null;
            lock(gate)
            {
                if(stopping&&queue.Count==0)break;
                if(queue.Count>0)item=queue.Dequeue();
            }
            if(item!=null)
            {
                try
                {
                    if(item.Deadline){BeforeDeadline?.Invoke(item.At);lock(gate)deadlineQueued=false;watermark=item.At;detector.Advance(item.At,clock.Now);}
                    else if(item.Discontinuity){lock(gate)overflow=false;watermark=item.At;input.SignalDiscontinuity(clock.Now);}
                    else if(item.Pointer is{} pointer)
                    {
                        bool late=item.At<watermark||clock.Now-item.At>MaximumInputAge;
                        if(late){input.SignalDiscontinuity(clock.Now);if(pointer.Kind==RawPointerKind.Move)continue;}
                        watermark=item.At>watermark?item.At:watermark;
                        input.Process(pointer,late?clock.Now:item.At);
                    }
                    else{watermark=item.At>watermark?item.At:watermark;item.Action?.Invoke();}
                }
                catch(Exception ex){Error?.Invoke(ex);try{input.SignalDiscontinuity(clock.Now);}catch{}}
            }
            due=detector.NextDeadline;
            if(due is{} time&&clock.Now>=time)
            {
                lock(gate)if(!stopping&&!deadlineQueued){deadlineQueued=true;queue.Enqueue(new(clock.Now,null,null,Deadline:true));}
                wake.Set();
            }
            lock(gate){if(queue.Count>0)continue;if(stopping)break;}
            TimeSpan wait=due is{} deadline?deadline-clock.Now:Timeout.InfiniteTimeSpan;
            if(wait!=Timeout.InfiniteTimeSpan&&wait<=TimeSpan.Zero)continue;
            wake.WaitOne(wait==Timeout.InfiniteTimeSpan?wait:TimeSpan.FromMilliseconds(Math.Clamp(Math.Ceiling(wait.TotalMilliseconds),1,3600000)));
        }
    }
    public void Dispose()
    {
        lock(gate)stopping=true;wake.Set();
        if(!thread.Join(TimeSpan.FromSeconds(10)))throw new TimeoutException("Domain loop did not stop.");
        wake.Dispose();
    }
}
