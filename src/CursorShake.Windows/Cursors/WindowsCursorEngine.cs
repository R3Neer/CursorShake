using CursorShake.Core.Cursors;
using CursorShake.Core.Runtime;
using CursorShake.Windows.Cursors.Formats;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;
using Microsoft.Win32;

namespace CursorShake.Windows.Cursors;

public enum CursorEngineStatus { Starting,Available,Hidden,Unknown,Incompatible,ExternalChange,Degraded,Stopped }
public sealed record ThemeApplyResult(bool Applied,string? Error=null);
internal readonly record struct CursorObservation(CursorRole? Role,bool Visible,nint Monitor,int NormalSize);
internal readonly record struct CursorEngineSnapshot(CursorRole? ActiveRole,TimeSpan ActivatedAt,CursorEngineStatus Status,long HardEpoch,long PublishedFrames);

/// <summary>One native publisher and one latest scale target; rasterization never blocks capture or the domain loop.</summary>
public sealed class WindowsCursorEngine : ICursorEngine,IDisposable
{
    private readonly object gate=new();
    private readonly AutoResetEvent wake=new(false);
    private readonly Queue<Action> commands=new();
    private readonly Thread worker;
    private readonly NativeCursorSession session;
    private readonly IMonotonicClock clock;
    private readonly RasterCache cache=new();
    private readonly Dictionary<CursorRole,(CursorFrame Frame,int Width,int Height)> nativeStates=new();
    private readonly HashSet<CursorRole> normalize=new();
    private PreparedTheme? theme;
    private CursorRole? activeRole;
    private TimeSpan activatedAt,lastActivePublication;
    private bool hasActivePublication;
    private double targetScale=1,maximumScale=16;
    private long hardEpoch;
    private int normalSize,pendingNormalSize;
    private nint monitor;
    private bool compatible=true,visible,stopping,disposed,finished,rendererFailed;
    private Task<RestoreResult>? stopTask;
    private sealed record ScaleRequest(ScaleSample Sample);
    private ScaleRequest? latestScaleRequest;
    private CursorEngineStatus status=CursorEngineStatus.Starting;
    public WindowsCursorEngine(string stateDirectory,string guardExecutablePath,IMonotonicClock clock)
    {
        this.clock=clock;normalSize=NormalSizeForDpi(CursorNative.GetDpiForSystem());
        session=new NativeCursorSession(stateDirectory,guardExecutablePath);
        worker=new Thread(Run){Name="CursorShake renderer",IsBackground=true};worker.Start();
    }
    internal Action? BeforeThemePublish {get;set;}
    internal Action<CursorRole>? BeforeNativePublish {get;set;}
    internal CursorEngineSnapshot Snapshot {get{lock(gate)return new(activeRole,activatedAt,status,hardEpoch,PublishedFrames);}}
    public event Action<CursorEngineStatus>? StatusChanged;
    public event Action<string>? Error;
    public CursorEngineStatus Status {get{lock(gate)return status;}}
    public long RasterBytes {get{lock(gate)return cache.Bytes;}}
    public long PublishedFrames {get;private set;}
    public TimeSpan MaximumPublicationLatency {get;private set;}
    public long BaselineBytes=>session.BaselineEstimatedBytes;
    public void SetScale(ScaleSample sample)
    {
        if(!double.IsFinite(sample.Scale)||sample.Scale<1)throw new ArgumentOutOfRangeException(nameof(sample));
        if(Volatile.Read(ref stopping)||Volatile.Read(ref rendererFailed))return;
        Interlocked.Exchange(ref latestScaleRequest,new ScaleRequest(sample));
        if(Volatile.Read(ref stopping)||Volatile.Read(ref rendererFailed)){Interlocked.Exchange(ref latestScaleRequest,null);return;}
        try{wake.Set();}catch(ObjectDisposedException)when(Volatile.Read(ref stopping)){}
    }
    private void ConsumeLatestScale()
    {
        var request=Interlocked.Exchange(ref latestScaleRequest,null);
        if(request==null)return;
        var sample=request.Sample;
        lock(gate)
        {
            if(stopping||rendererFailed||(!compatible&&sample.Scale>targetScale))return;
            targetScale=sample.Scale;
            if(sample.Scale==1&&pendingNormalSize is >0 and <=2048)
            {
                normalSize=pendingNormalSize;pendingNormalSize=0;hardEpoch++;
                if(activeRole is{} role)normalize.Add(role);
            }
        }
    }
    public Task<ThemeApplyResult> ApplyThemeAsync(PreparedTheme prepared,double maxScale)
    {
        var result=new TaskCompletionSource<ThemeApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(()=>
        {
            try{Apply(prepared,maxScale);result.SetResult(new(true));}
            catch(Exception ex){result.SetResult(new(false,ex.Message));}
        });return result.Task;
    }
    private void Apply(PreparedTheme prepared,double maxScale)
    {
        lock(gate){if(stopping)throw new ObjectDisposedException(nameof(WindowsCursorEngine));if(rendererFailed)throw new InvalidOperationException("Renderer is degraded; restart before applying a theme.");}
        if(!double.IsFinite(maxScale)||maxScale<1)throw new ArgumentOutOfRangeException(nameof(maxScale));
        ValidateMonitors(maxScale);
        if(prepared.EstimatedBytes+(theme?.EstimatedBytes??0)+session.BaselineEstimatedBytes>CursorAssetLoader.MaximumSourceBytes)
            throw new CursorAssetException(AssetErrorCode.LimitExceeded,"Active, candidate and baseline sources exceed64MiB.");
        var previous=theme;cache.Clear();
        lock(gate){hardEpoch++;targetScale=1;Interlocked.Exchange(ref latestScaleRequest,null);}
        try
        {
            BeforeThemePublish?.Invoke();
            foreach(var role in CursorRoles.All)
            {
                var asset=prepared.Roles[role];var source=asset.Frames[asset.Steps[0].FrameIndex];
                Publish(role,source,normalSize,0);
            }
        }
        catch
        {
            if(previous!=null&&!session.IsExternalChangeDetected)
            {
                try{foreach(var role in CursorRoles.All){var a=previous.Roles[role];Publish(role,a.Frames[a.Steps[0].FrameIndex],normalSize,0);}}
                catch(Exception rollback)
                {
                    if(session.IsExternalChangeDetected)SetStatus(CursorEngineStatus.ExternalChange);
                    else{MarkFailed();Error?.Invoke("Theme rollback failed: "+rollback.Message);}
                }
            }
            else if(session.IsExternalChangeDetected)SetStatus(CursorEngineStatus.ExternalChange);else MarkFailed();
            throw;
        }
        theme=prepared;maximumScale=maxScale;compatible=true;activatedAt=clock.Now;
        lock(gate){hardEpoch++;normalize.Clear();}
        previous?.Dispose();
        ObserveSystemCursor();
    }
    public void ObserveSystemCursor()
    {
        var info=new CursorNative.CursorInfo{Size=System.Runtime.InteropServices.Marshal.SizeOf<CursorNative.CursorInfo>()};
        if(!CursorNative.GetCursorInfo(ref info))return;
        CursorRole? role=null;
        foreach(var pair in session.SharedHandles)if(pair.Value==info.Cursor){role=pair.Key;break;}
        var nextMonitor=CursorNative.MonitorFromPoint(info.Position,2);int size;
        lock(gate)size=normalSize;
        if(nextMonitor!=monitor)
        {
            uint dpi=CursorNative.GetDpiForSystem();
            try{if(CursorNative.GetDpiForMonitor(nextMonitor,0,out var x,out _)==0)dpi=x;}catch(DllNotFoundException){}
            size=NormalSizeForDpi(dpi);
        }
        Observe(new(role,(info.Flags&1)!=0,nextMonitor,size));
    }
    internal void Observe(CursorObservation observation)
    {
        var role=observation.Role;bool show=observation.Visible;var nextMonitor=observation.Monitor;int size=observation.NormalSize;
        if(size is <1 or >2048)throw new ArgumentOutOfRangeException(nameof(observation));
        CursorEngineStatus nextStatus;
        lock(gate)
        {
            if(stopping)return;
            if(role!=activeRole||show!=visible||nextMonitor!=monitor)
            {
                if(activeRole is{} previous&&(role!=activeRole||!show))normalize.Add(previous);
                if(role!=activeRole||(!visible&&show))activatedAt=clock.Now;
                activeRole=role;visible=show;monitor=nextMonitor;hardEpoch++;
            }
            if((long)Math.Ceiling(size*maximumScale)>2048)
            {
                if(compatible)Error?.Invoke($"El DPI actual supera el límite de2048px. Multiplicador compatible: ×{2048.0/size:0.##}.");
                compatible=false;pendingNormalSize=size;
            }
            else
            {
                compatible=true;
                if(normalSize!=size){normalSize=size;hardEpoch++;}
            }
            nextStatus=rendererFailed?CursorEngineStatus.Degraded:session.IsExternalChangeDetected?CursorEngineStatus.ExternalChange:theme==null?CursorEngineStatus.Starting:!show?CursorEngineStatus.Hidden:role==null?CursorEngineStatus.Unknown:!compatible?CursorEngineStatus.Incompatible:CursorEngineStatus.Available;
        }
        SetStatus(nextStatus);wake.Set();
    }
    public void NotifyDisplayChanged()
    {
        lock(gate){monitor=0;hardEpoch++;}
        ObserveSystemCursor();
    }
    public void CheckExternalChanges()=>Enqueue(()=>
    {
        if(session.CheckForExternalChanges()){SetStatus(CursorEngineStatus.ExternalChange);lock(gate){targetScale=1;hardEpoch++;}}
    });
    public void Normalize()=>Enqueue(()=>
    {
        if(theme==null||session.IsExternalChangeDetected||rendererFailed)return;
        foreach(var role in CursorRoles.All){var a=theme.Roles[role];Publish(role,a.Frames[a.Steps[0].FrameIndex],normalSize,0);}
    });
    private static int NormalSizeForDpi(uint dpi)
    {
        object? configured=Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Cursors","CursorBaseSize",32);
        int logical=configured is int i&&i>=16&&i<=2048?i:32;
        return Math.Max(1,(int)Math.Round(logical*(dpi==0?96:dpi)/96.0));
    }
    private void ValidateMonitors(double scale)
    {
        foreach(var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var point=new CursorNative.Point{X=screen.Bounds.Left+1,Y=screen.Bounds.Top+1};var m=CursorNative.MonitorFromPoint(point,2);
            uint dpi=CursorNative.GetDpiForSystem();try{if(CursorNative.GetDpiForMonitor(m,0,out var x,out _)==0)dpi=x;}catch(DllNotFoundException){}
            int size=NormalSizeForDpi(dpi);
            if(Math.Ceiling(size*scale)>2048)throw new CursorAssetException(AssetErrorCode.LimitExceeded,$"Maximum scale exceeds2048px on {screen.DeviceName}. Compatible multiplier: ×{2048.0/size:0.##}.");
        }
    }
    private long NativeBytes=>nativeStates.Values.Sum(x=>(long)x.Width*x.Height*4+((x.Width+31)/32)*4L*x.Height);
    private void Publish(CursorRole role,CursorFrame source,int maxSide,long expectedEpoch)
    {
        double ratio=maxSide/Math.Max(source.Width,source.Height);
        int width=Math.Max(1,(int)Math.Round(source.Width*ratio)),height=Math.Max(1,(int)Math.Round(source.Height*ratio));
        if(width>2048||height>2048)throw new CursorAssetException(AssetErrorCode.LimitExceeded,"Cursor exceeds2048px.");
        long bytes=(long)width*height*4+((width+31)/32)*4L*height;
        cache.Reserve(bytes,NativeBytes+bytes);
        if(!cache.TryGet(source,width,height,out var pixels)){pixels=source.Rasterize(width,height);cache.Add(source,width,height,pixels,NativeBytes+bytes);}
        lock(gate)
        {
            if(rendererFailed)throw new InvalidOperationException("Renderer is degraded; native mutation is blocked.");
            if(stopping){if(expectedEpoch==0)throw new OperationCanceledException("Native publication is stopping.");return;}
            if(expectedEpoch!=0&&expectedEpoch!=hardEpoch)return;
            BeforeNativePublish?.Invoke(role);
            session.Publish(role,pixels);nativeStates[role]=(source,width,height);PublishedFrames++;
        }
    }
    private void Run()
    {
        while(true)
        {
            try
            {
                ConsumeLatestScale();
                Action? command=null;
                lock(gate){if(commands.Count>0)command=commands.Dequeue();}
                if(command!=null){command();if(finished)break;continue;}
                PreparedTheme? current;CursorRole? role;double scale;TimeSpan origin;long epoch;int size;bool available;
                lock(gate){current=theme;role=activeRole;scale=targetScale;origin=activatedAt;epoch=hardEpoch;size=normalSize;available=visible&&!session.IsExternalChangeDetected&&!stopping;}
                if(current==null||session.IsExternalChangeDetected||stopping||rendererFailed){wake.WaitOne();continue;}
                CursorRole? idleRole=null;
                lock(gate){if(normalize.Count>0){idleRole=normalize.First();normalize.Remove(idleRole.Value);}}
                if(idleRole is{} idle)
                {var a=current.Roles[idle];Publish(idle,a.Frames[a.Steps[0].FrameIndex],size,epoch);continue;}
                if(role is not{} active||!available){wake.WaitOne();continue;}
                var asset=current.Roles[active];var now=clock.Now;int index=asset.FrameIndexAt(now-origin);var frame=asset.Frames[index];
                int target=Math.Clamp((int)Math.Round(size*scale),1,2048);
                double ratio=target/Math.Max(frame.Width,frame.Height);int width=Math.Max(1,(int)Math.Round(frame.Width*ratio)),height=Math.Max(1,(int)Math.Round(frame.Height*ratio));
                if(!nativeStates.TryGetValue(active,out var previous)||!ReferenceEquals(previous.Frame,frame)||previous.Width!=width||previous.Height!=height)
                {
                    var began=clock.Now;
                    var delay=TimeSpan.FromSeconds(1.0/60)-(began-lastActivePublication);
                    if(hasActivePublication&&delay>TimeSpan.Zero){wake.WaitOne(delay);continue;}
                    Publish(active,frame,target,epoch);lastActivePublication=clock.Now;hasActivePublication=true;var latency=clock.Now-began;
                    if(latency>MaximumPublicationLatency)MaximumPublicationLatency=latency;
                }
                if(asset.IsAnimated)
                {
                    long offset=Math.Max(0,(clock.Now-origin).Ticks)%asset.Duration.Ticks,cumulative=0;long delay=TimeSpan.TicksPerSecond/60;
                    foreach(var step in asset.Steps){cumulative+=step.Duration.Ticks;if(cumulative>offset){delay=cumulative-offset;break;}}
                    wake.WaitOne(TimeSpan.FromTicks(Math.Max(1,delay)));
                }
                else wake.WaitOne();
            }
            catch(Exception ex)
            {
                if(session.IsExternalChangeDetected)
                {
                    // Native ownership was ceded, so no later publication or automatic baseline adoption
                    // is allowed. This is a semantic external change, not a failed rasterizer.
                    lock(gate){targetScale=1;hardEpoch++;normalize.Clear();Interlocked.Exchange(ref latestScaleRequest,null);}
                    SetStatus(CursorEngineStatus.ExternalChange);
                }
                else
                {
                    MarkFailed();
                    try{Error?.Invoke(ex.ToString());}catch{}
                }
                // Keep the command loop alive: Stop must still drain and attempt owned restoration.
            }
        }
    }
    private void Enqueue(Action command)
    {
        lock(gate){if(stopping)throw new ObjectDisposedException(nameof(WindowsCursorEngine));commands.Enqueue(command);}
        wake.Set();
    }
    private void MarkFailed()
    {lock(gate){Volatile.Write(ref rendererFailed,true);targetScale=1;hardEpoch++;Interlocked.Exchange(ref latestScaleRequest,null);}SetStatus(CursorEngineStatus.Degraded);}
    private void SetStatus(CursorEngineStatus value)
    {
        bool changed;lock(gate){if(rendererFailed&&value!=CursorEngineStatus.Stopped)value=CursorEngineStatus.Degraded;changed=status!=value;status=value;}
        if(changed)StatusChanged?.Invoke(value);
    }
    public Task<RestoreResult> StopAsync()
    {
        lock(gate)
        {
            if(stopTask!=null)return stopTask;
            Volatile.Write(ref stopping,true);hardEpoch++;Interlocked.Exchange(ref latestScaleRequest,null);
            var result=new TaskCompletionSource<RestoreResult>(TaskCreationOptions.RunContinuationsAsynchronously);stopTask=result.Task;
            commands.Enqueue(()=>
            {
                try{var restored=session.RestoreOwned();theme?.Dispose();theme=null;cache.Clear();SetStatus(CursorEngineStatus.Stopped);result.SetResult(restored);}
                catch(Exception ex){result.SetException(ex);}
                finally{finished=true;}
            });
            wake.Set();return stopTask;
        }
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{StopAsync().GetAwaiter().GetResult();}
        finally{worker.Join();try{session.Dispose();}finally{wake.Dispose();}}
    }
}
