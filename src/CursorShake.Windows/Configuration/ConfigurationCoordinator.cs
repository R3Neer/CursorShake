using CursorShake.Core.Runtime;
using CursorShake.Core.Shake;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Input;

namespace CursorShake.Windows.Configuration;

public sealed record ConfigurationUpdate(AppConfiguration? Effective,string? PendingTheme,long Revision,bool HasError);

/// <summary>One preparation pipeline with a latest descriptor; commits the theme/profile together at normal scale.</summary>
public sealed class ConfigurationCoordinator : IAsyncDisposable
{
    private readonly object gate=new();
    private readonly WorkspaceStore workspace;
    private readonly ThemePreparer preparer;
    private readonly WindowsCursorEngine engine;
    private readonly PointerInput input;
    private readonly ShakeDetector detector;
    private readonly PointerRuntimeLoop runtime;
    private readonly IMonotonicClock clock;
    private readonly Func<bool> remainSuspended;
    private readonly FileMonitor monitor;
    private CancellationTokenSource cancellation=new();
    private Task? running;
    private TaskCompletionSource? idleWait;
    private long wanted,completed;
    private bool stopping,hasError;
    private volatile bool applying;
    public bool IsApplying=>applying;
    private AppConfiguration? effective;
    private string? pendingTheme;
    public ConfigurationCoordinator(WorkspaceStore workspace,ThemePreparer preparer,WindowsCursorEngine engine,PointerInput input,ShakeDetector detector,PointerRuntimeLoop runtime,IMonotonicClock clock,Func<bool> remainSuspended)
    {
        this.workspace=workspace;this.preparer=preparer;this.engine=engine;this.input=input;this.detector=detector;this.runtime=runtime;this.clock=clock;this.remainSuspended=remainSuspended;
        try{workspace.EnsureConfiguration();}
        catch(Exception ex)when(Directory.Exists(workspace.RootPath)&&(ex is IOException or ConfigurationException or System.Text.DecoderFallbackException))
        {
            // A present but invalid/busy file must still be watched; Process reports it without replacing it.
        }
        monitor=new FileMonitor(workspace.RootPath);monitor.Changed+=RequestReload;monitor.Error+=OnMonitorError;
        workspace.RootChanged+=OnRootChanged;detector.StateChanged+=OnDetectorStateChanged;
    }
    public event Action<ConfigurationUpdate>? Changed;
    public event Action<Exception>? Error;
    public AppConfiguration? Effective {get{lock(gate)return effective;}}
    public string? PendingTheme {get{lock(gate)return pendingTheme;}}
    public Task InitializeAsync(){RequestReload();lock(gate)return running??Task.CompletedTask;}
    public void RequestReload()
    {
        lock(gate)
        {
            if(stopping)return;wanted++;cancellation.Cancel();
            if(running==null||running.IsCompleted)running=Task.Run(Process);
        }
    }
    private void OnMonitorError(Exception error){Error?.Invoke(error);RequestReload();}
    private void OnRootChanged(){monitor.ReconfigureRoot(workspace.RootPath);RequestReload();}
    private void OnDetectorStateChanged()
    {
        lock(gate){if(detector.CanApplyProfile)idleWait?.TrySetResult();}
    }
    private async Task Process()
    {
        while(true)
        {
            long revision,generation;CancellationToken token;
            lock(gate)
            {
                if(stopping||wanted==completed){running=null;return;}
                revision=wanted;cancellation.Dispose();cancellation=new();token=cancellation.Token;generation=workspace.Generation;
            }
            PreparedTheme? candidate=null;
            try
            {
                AppConfiguration configuration;bool validCandidate=true;
                try{configuration=ConfigParser.Parse(workspace.EnsureConfiguration(),workspace.ConfigPath);}
                catch(Exception ex)
                {
                    validCandidate=false;lock(gate)hasError=true;Error?.Invoke(ex);
                    lock(gate){if(effective!=null)throw;}
                    configuration=new AppConfiguration();
                }
                token.ThrowIfCancellationRequested();
                candidate=preparer.Prepare(configuration,workspace.RootPath,token);
                lock(gate)
                {
                    if(revision!=wanted||generation!=workspace.Generation)throw new OperationCanceledException();
                    pendingTheme=configuration.Theme;
                }
                PublishStatus(revision);
                while(true)
                {
                    var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    bool eligible=false;
                    await runtime.InvokeAsync(()=>
                    {
                        lock(gate){idleWait=ready;}
                        eligible=detector.CanApplyProfile;
                        if(eligible)ready.TrySetResult();
                    }).ConfigureAwait(false);
                    await ready.Task.WaitAsync(token).ConfigureAwait(false);
                    bool admitted=false;
                    await runtime.InvokeAsync(time=>
                    {
                        lock(gate)
                        {
                            if(stopping||revision!=wanted||generation!=workspace.Generation)return;
                            if(!detector.CanApplyProfile)return;
                            applying=true;detector.SetSuspended(true,time.ApplyAt);admitted=true;
                        }
                    }).ConfigureAwait(false);
                    if(admitted)break;
                    token.ThrowIfCancellationRequested();
                }
                // Once native publication starts, finish/compensate it before accepting a newer candidate.
                var applied=await engine.ApplyThemeAsync(candidate,configuration.Gesture.Growth.MaximumScale).ConfigureAwait(false);
                if(!applied.Applied)throw new ConfigurationException("ThemeApplyFailed",applied.Error??"Unable to apply cursor theme.",workspace.ConfigPath);
                candidate=null; // Engine owns the prepared resource graph now.
                await runtime.InvokeAsync(time=>
                {
                    input.ApplyProfile(configuration.Gesture);
                    detector.ApplyProfile(configuration.Gesture,time.OccurredAt,time.ApplyAt);
                    applying=false;
                    detector.SetSuspended(stopping||remainSuspended(),time.ApplyAt);
                }).ConfigureAwait(false);
                lock(gate){effective=configuration;pendingTheme=null;hasError=!validCandidate;}
                if(workspace.CleanupPending)workspace.CompletePendingMove();
                PublishStatus(revision);
            }
            catch(OperationCanceledException){}
            catch(Exception ex)
            {
                lock(gate)hasError=true;
                Error?.Invoke(ex);
                try{await runtime.InvokeAsync(time=>{applying=false;detector.SetSuspended(stopping||remainSuspended(),time.ApplyAt);}).ConfigureAwait(false);}catch(ObjectDisposedException){}
            }
            finally
            {
                candidate?.Dispose();
                lock(gate){completed=revision;idleWait=null;if(revision==wanted)pendingTheme=null;}
                PublishStatus(revision);
            }
        }
    }
    private void PublishStatus(long revision)
    {
        ConfigurationUpdate state;lock(gate)state=new(effective,pendingTheme,revision,hasError);
        Changed?.Invoke(state);
    }
    public Task ChangeThemeAsync(string id)=>Task.Run(()=>
    {
        var current=workspace.Read();workspace.ProtectedUpdate(text=>ConfigurationEdits.SetTheme(text,id),current.Revision);RequestReload();
    });
    public Task MoveWorkspaceAsync(string destination)=>Task.Run(()=>
    {
        workspace.MoveTo(destination,root=>
        {
            var config=ConfigParser.Parse(ProtectedFile.Decode(WorkspaceResources.ReadContained(root,"config.toml",ConfigParser.MaximumTextBytes)),Path.Combine(root,"config.toml"));
            using var prepared=preparer.Prepare(config,root);
        });
        RequestReload();
    });
    public async ValueTask DisposeAsync()
    {
        Task? task;
        lock(gate){if(stopping)return;stopping=true;cancellation.Cancel();idleWait?.TrySetCanceled();task=running;}
        monitor.Dispose();workspace.RootChanged-=OnRootChanged;detector.StateChanged-=OnDetectorStateChanged;
        if(task!=null)try{await task.ConfigureAwait(false);}catch(OperationCanceledException){}
        cancellation.Dispose();
    }
}
