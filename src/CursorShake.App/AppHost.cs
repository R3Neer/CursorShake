using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CursorShake.App.Dialogs;
using CursorShake.App.Tray;
using CursorShake.Core.Shake;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Formats;
using CursorShake.Windows.Input;
using CursorShake.Windows.Lifecycle;
using CursorShake.Windows.Recovery;
using Microsoft.Win32;

namespace CursorShake.App;

public sealed record AppHostOptions
{
    private static string DataRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CursorShake");
    public string WorkspaceRoot {get;init;}=DataRoot;
    public string StateDirectory {get;init;}=Path.Combine(DataRoot,"state");
    public string BundledThemes {get;init;}=Path.Combine(AppContext.BaseDirectory,"themes");
    public string GuardExecutable {get;init;}=Path.Combine(AppContext.BaseDirectory,"CursorShake.RecoveryGuard.exe");
    public string ExecutablePath {get;init;}=Environment.ProcessPath??Path.Combine(AppContext.BaseDirectory,"CursorShake.exe");
    public bool PersistLocator {get;init;}=true;
    public Action<UiDiagnostic>? DiagnosticPresenter {get;init;}
}

/// <summary>Composition, lifetime and UI commands. The input/domain loop owns all gesture state.</summary>
public sealed class AppHost : ApplicationContext
{
    private readonly Control dispatch=new();
    private readonly AppHostOptions options;
    private readonly DiagnosticCenter diagnostics;
    private readonly TrayView tray;
    private readonly Icon icon;
    private readonly StopwatchClock clock=new();
    private readonly WorkspaceStore workspace;
    private readonly ThemePreparer preparer;
    private readonly StartupRegistration startup;
    private PointerInput? input;
    private ShakeDetector? detector;
    private PointerRuntimeLoop? runtime;
    private WindowsPointerSource? source;
    private WindowsCursorEngine? engine;
    private CursorObservationSource? observation;
    private SessionNotificationSource? sessionNotifications;
    private ConfigurationCoordinator? coordinator;
    private volatile bool closing,userPaused,engineUnavailable=true;
    private readonly SessionAvailability session=new(Process.GetCurrentProcess().SessionId);
    private bool initializationFailed,lifecycleBusy,starting,maintenanceShutdown;
    private bool startupInitialized;
    private readonly StartupTimingProbe? startupTiming;
    private bool startupEnabled,systemEventsSubscribed;
    private AppConfiguration? effective;
    private AppLanguage initialLanguage=AppLanguage.English;
    private AppLanguage UiLanguage=>effective?.Language??initialLanguage;
    private string? pending;
    private string statusText="Iniciando…";
    private Task initialization=Task.CompletedTask;
    private Task retryOperation=Task.CompletedTask;
    private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopped=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready=>ready.Task;
    public Task Stopped=>stopped.Task;
    public IReadOnlyList<UiDiagnostic> CurrentDiagnostics=>diagnostics.Current;
    public AppHost(AppHostOptions? options=null)
    {
        this.options=options??new();
        startupTiming=this.options.PersistLocator?StartupTimingProbe.TryBegin(this.options.ExecutablePath):null;
        // Validate persistent state before allocating a tray icon or native UI handle.
        workspace=new WorkspaceStore(this.options.WorkspaceRoot,this.options.StateDirectory,this.options.PersistLocator);
        preparer=new ThemePreparer(this.options.BundledThemes);startup=new StartupRegistration(this.options.ExecutablePath);
        try{initialLanguage=ConfigParser.Parse(workspace.Read().Text,workspace.ConfigPath).Language;}catch{}
        Icon? allocatedIcon=null;TrayView? allocatedTray=null;DiagnosticCenter? allocatedDiagnostics=null;
        try
        {
        _ =dispatch.Handle;
        diagnostics=allocatedDiagnostics=new DiagnosticCenter(OpenPath,Retry,this.options.DiagnosticPresenter,()=>UiLanguage,RetryForDialogAsync);
        diagnostics.RecoveryResolver=ResolveRecovery;
        diagnostics.ActionResolver=ResolveDiagnosticAction;
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("CursorShake.Icon");
        icon=allocatedIcon=resource!=null?new Icon(resource):(Icon)SystemIcons.Application.Clone();
        tray=allocatedTray=new TrayView(icon);tray.ActionRequested+=OnAction;diagnostics.Changed+=UpdateTray;
        workspace.Diagnostic+=d=>Post(()=>{if(!closing)diagnostics.Report(WorkspaceIssue(d));});
        try{startupEnabled=startup.IsEnabled();}catch(Exception ex){Report(ex,"Inicio con Windows");}
        UpdateTray();Post(()=>initialization=StartAsync());
        }
        catch{allocatedTray?.Dispose();allocatedDiagnostics?.Dispose();allocatedIcon?.Dispose();dispatch.Dispose();throw;}
    }
    private async Task StartAsync()
    {
        if(starting)throw new InvalidOperationException("An application initialization is already running.");
        starting=true;startupInitialized=false;
        try
        {
            if(closing)return;
            initializationFailed=false;
            if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)||RuntimeInformation.OSArchitecture!=Architecture.X64||SystemInformation.TerminalServerSession)
                throw new PlatformNotSupportedException("El efecto se admite inicialmente en una sesión local de Windows11x64. No se han modificado cursores.");
            var pendingRecoveries=RecoveryBootstrap.Inspect(options.StateDirectory).Where(x=>x.BlocksNewSession).ToArray();
            if(pendingRecoveries.Length>0)
                throw new ConfigurationException("RecoveryRequired","Existe una recuperación anterior pendiente. No se capturará un baseline nuevo sobre recursos de procedencia incierta.\n"+string.Join(Environment.NewLine,pendingRecoveries.Select(x=>x.Summary)),pendingRecoveries[0].DirectoryPath);
            engine=await Task.Run(()=>new WindowsCursorEngine(options.StateDirectory,options.GuardExecutable,clock));
            if(closing)return;
            input=new PointerInput(new GestureProfile(),()=>source?.CurrentButtons??WindowsPointerSource.ReadButtons());detector=new ShakeDetector(input,engine,clock);
            runtime=new PointerRuntimeLoop(clock,input,detector);
            runtime.Error+=ex=>Report(ex,"Entrada");engine.Error+=message=>Report(new InvalidOperationException(message),"Cursor");
            engine.StatusChanged+=OnEngineStatus;
            coordinator=new ConfigurationCoordinator(workspace,preparer,engine,input,detector,runtime,clock,ShouldRemainSuspended);
            coordinator.Error+=ex=>Report(ex,"Configuración",workspace.ConfigPath);
            coordinator.Changed+=OnConfiguration;
            observation=new CursorObservationSource(()=>engine?.ObserveSystemCursor());
            sessionNotifications=new SessionNotificationSource(OnSessionSwitch);SystemEvents.PowerModeChanged+=OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged+=OnDisplayChanged;SystemEvents.UserPreferenceChanged+=OnUserPreferenceChanged;
            systemEventsSubscribed=true;
            await coordinator.InitializeAsync();
            if(closing)return;
            source=new WindowsPointerSource(runtime.Accept);
            await runtime.InvokeAsync(time=>input.SignalDiscontinuity(time.OccurredAt));
            engine.ObserveSystemCursor();
            statusText=effective==null?"Configuración con errores":StatusLabel(engine.Status);
            ShowWorkspaceNotices();
            startupInitialized=true;
            UpdateTray();
        }
        catch(Exception ex)
        {
            initializationFailed=true;
            var cleanupFailures=new List<Exception>();await StopComponentsAsync(cleanupFailures);
            statusText="Efecto desactivado";Report(ex,"Inicio");
            foreach(var failure in cleanupFailures)Report(failure,"Recuperación");
            UpdateTray();
        }
        finally{starting=false;ready.TrySetResult();}
    }
    private bool ShouldRemainSuspended()=>closing||userPaused||session.IsUnavailable||engineUnavailable||(coordinator?.IsApplying??false);
    private void OnEngineStatus(CursorEngineStatus status)
    {
        engineUnavailable=status!=CursorEngineStatus.Available;
        var loop=runtime;var gesture=detector;
        if(loop!=null&&gesture!=null&&!closing)_=ObserveTask(loop.InvokeAsync(time=>gesture.SetSuspended(ShouldRemainSuspended(),time.ApplyAt)),"Cursor");
        Post(()=>{statusText=StatusLabel(status);UpdateTray();});
    }
    private static string StatusLabel(CursorEngineStatus value)=>value switch
    {
        CursorEngineStatus.Available=>"Activo",CursorEngineStatus.Hidden=>"Cursor oculto",CursorEngineStatus.Unknown=>"Cursor privado de la aplicación",
        CursorEngineStatus.Incompatible=>"DPI incompatible: crecimiento pausado",CursorEngineStatus.ExternalChange=>"Cambio externo: control cedido",
        CursorEngineStatus.Degraded=>"Motor detenido por un error",CursorEngineStatus.Stopped=>"Detenido",_=>"Iniciando…"
    };
    private void OnConfiguration(ConfigurationUpdate update)=>Post(()=>
    {
        effective=update.Effective;pending=update.PendingTheme;
        diagnostics.RefreshLanguage();
        if(update.Effective!=null&&!update.HasError)diagnostics.ClearArea("Configuración");
        UpdateTray();
    });
    private void UpdateTray()
    {
        if(closing||dispatch.IsDisposed)return;
        var configuration=effective??new AppConfiguration();
        var themes=preparer.AvailableThemes(configuration).ToDictionary(id=>id,id=>id switch{"windows"=>"Windows","bibata-ice"=>"Bibata Ice","nordzy-white"=>"Nordzy blanco",_=>id},StringComparer.OrdinalIgnoreCase);
        tray.Update(new(){Language=UiLanguage,ThemeId=configuration.Theme,Paused=userPaused||session.IsUnavailable,StartupEnabled=startupEnabled,HasErrors=diagnostics.HasErrors,PendingTheme=pending,StatusText=userPaused?"Pausado":statusText,Themes=themes});
        if(startupInitialized && !initializationFailed && effective!=null && source!=null && !ShouldRemainSuspended())startupTiming?.RecordOperational();
    }
    private async void OnAction(TrayAction action)
    {
        if(closing)return;
        try
        {
            switch(action.Kind)
            {
                case TrayActionKind.OpenConfiguration: workspace.EnsureConfiguration();OpenPath(workspace.ConfigPath);break;
                case TrayActionKind.OpenWorkspace: OpenPath(workspace.RootPath);break;
                case TrayActionKind.ChooseWorkspace:
                    if(coordinator==null)throw new InvalidOperationException("La configuración todavía no está preparada.");
                    using(var picker=new FolderBrowserDialog{Description=(effective?.Language??AppLanguage.English)==AppLanguage.Spanish?"Carpeta para config.toml y sus temas personales":"Folder for config.toml and personal themes",UseDescriptionForTitle=true,SelectedPath=workspace.RootPath})
                        if(picker.ShowDialog()==DialogResult.OK)await coordinator.MoveWorkspaceAsync(picker.SelectedPath);
                    break;
                case TrayActionKind.SelectTheme:
                    if(coordinator==null)throw new InvalidOperationException("El motor todavía no está preparado.");
                    if(action.ThemeId is{} id)await coordinator.ChangeThemeAsync(id);break;
                case TrayActionKind.Preview:
                    if(runtime==null||detector==null)throw new InvalidOperationException("El motor no está preparado.");
                    await runtime.InvokeAsync(time=>{if(!detector.TryStartPreview(time.OccurredAt,time.ApplyAt))throw new InvalidOperationException("La prueba requiere reposo, cursor compatible y botones libres.");});break;
                case TrayActionKind.TogglePause:
                    userPaused=!userPaused;
                    if(runtime!=null&&detector!=null)await runtime.InvokeAsync(time=>detector.SetSuspended(ShouldRemainSuspended(),time.ApplyAt));UpdateTray();break;
                case TrayActionKind.ToggleStartup:
                    await Task.Run(()=>startup.SetEnabled(!startupEnabled));startupEnabled=startup.IsEnabled();diagnostics.ClearArea("Inicio con Windows");UpdateTray();break;
                case TrayActionKind.ShowErrors: diagnostics.ShowErrors();break;
                case TrayActionKind.Exit: await StopAsync();break;
            }
        }
        catch(Exception ex){Report(ex,action.Kind==TrayActionKind.ToggleStartup?"Inicio con Windows":"Configuración",workspace.ConfigPath);}
    }
    public Task PreviewAsync()=>runtime!=null&&detector!=null?runtime.InvokeAsync(time=>{if(!detector.TryStartPreview(time.OccurredAt,time.ApplyAt))throw new InvalidOperationException("Preview unavailable.");}):Task.FromException(new InvalidOperationException("Not ready."));
    public Task PauseAsync(bool paused)
    {userPaused=paused;return runtime!=null&&detector!=null?runtime.InvokeAsync(time=>detector.SetSuspended(ShouldRemainSuspended(),time.ApplyAt)):Task.CompletedTask;}
    private Task RetryForDialogAsync(){Retry();return retryOperation;}
    private async void Retry()
    {
        if(closing||lifecycleBusy||!initialization.IsCompleted)return;
        lifecycleBusy=true;
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);retryOperation=completion.Task;
        try
        {
            await Task.Run(()=>workspace.RecoverPendingOperations());
            if(closing)return;
            ShowWorkspaceNotices();
            if(!initializationFailed&&coordinator!=null){coordinator.RequestReload();return;}
            foreach(var pendingRecovery in RecoveryBootstrap.Inspect(options.StateDirectory).Where(x=>x.BlocksNewSession))
            {
                if(!pendingRecovery.CanAttemptRecovery)throw new ConfigurationException("RecoveryRequired",pendingRecovery.Summary,pendingRecovery.DirectoryPath);
                var restored=await Task.Run(()=>RecoveryBootstrap.TryRecover(pendingRecovery.DirectoryPath));
                if(!restored.AllOwnedRestored)throw new ConfigurationException("RecoveryRequired",string.Join(Environment.NewLine,restored.Diagnostics),pendingRecovery.DirectoryPath);
                if(closing)return;
            }
            diagnostics.ClearArea("Inicio");initialization=StartAsync();await initialization;
        }
        catch(Exception ex){Report(ex,"Recuperación",options.StateDirectory);}
        finally{lifecycleBusy=false;completion.TrySetResult();}
    }
    private static UiDiagnostic WorkspaceIssue(WorkspaceDiagnostic notice)=>new("Archivos",notice.Code,"Hay una operación de archivos pendiente.",notice.Message+(notice.RelatedPaths is {Count:>0}?Environment.NewLine+string.Join(Environment.NewLine,notice.RelatedPaths):""),notice.Path,RelatedPaths:notice.RelatedPaths);
    private void ShowWorkspaceNotices()
    {
        if(closing)return;
        diagnostics.ClearArea("Archivos");
        foreach(var notice in workspace.RecoveryNotices)diagnostics.Report(WorkspaceIssue(notice));
    }
    private DiagnosticAction? ResolveDiagnosticAction(UiDiagnostic diagnostic)
    {
        if(diagnostic.Code!="RecoveryRequired"||diagnostic.Area is not ("Inicio" or "Recuperación")||engine!=null||!initializationFailed)return null;
        return new(DiagnosticText.T(UiLanguage,"Restore cursors…","Restaurar cursores…"),DiagnosticText.T(UiLanguage,"The cursor scheme saved in Windows will be restored. This can replace temporary cursor changes made by other utilities. Your CursorShake configuration and personal themes will be kept. Continue?","Se restaurarán los cursores guardados en Windows. Esto puede reemplazar cambios temporales de otras utilidades. Se conservarán tu configuración de CursorShake y tus temas personales. ¿Continuar?"),async()=>
        {
            if(closing||lifecycleBusy||engine!=null||!initializationFailed)throw new InvalidOperationException("La recuperación no está disponible en el estado actual.");
            lifecycleBusy=true;
            try
            {
                var result=await Task.Run(()=>RecoveryBootstrap.ResetToRegisteredScheme(options.StateDirectory));
                if(!result.AllOwnedRestored)throw new ConfigurationException("RecoveryRequired",string.Join(Environment.NewLine,result.Diagnostics),options.StateDirectory);
                if(closing)return;
                diagnostics.ClearArea("Inicio");diagnostics.ClearArea("Recuperación");
                initialization=StartAsync();await initialization;
            }
            finally{lifecycleBusy=false;}
        });
    }
    private RecoveryDialogContext? ResolveRecovery(UiDiagnostic diagnostic)
    {
        if(closing||diagnostic.Code!="RecoveryRequired"||diagnostic.RelatedPaths is not {Count:2} paths||
           !string.Equals(diagnostic.Path,workspace.ConfigPath,StringComparison.OrdinalIgnoreCase)||
           !paths.Any(p=>p.EndsWith(".before.toml",StringComparison.OrdinalIgnoreCase))||
           !paths.Any(p=>p.EndsWith(".candidate.toml",StringComparison.OrdinalIgnoreCase)))return null;
        string expectedPath=workspace.ConfigPath;
        return new(expectedPath,paths,()=>Task.Run(()=>
        {
            var snapshot=workspace.Read();
            if(!string.Equals(expectedPath,workspace.ConfigPath,StringComparison.OrdinalIgnoreCase))throw new IOException("La ubicación de configuración ha cambiado; vuelve a abrir el diagnóstico.");
            try
            {
                var config=ConfigParser.Parse(snapshot.Text,expectedPath);
                using var resources=preparer.Prepare(config,workspace.RootPath);
                return new RecoveryReview(snapshot.Text,snapshot.Revision,true);
            }
            catch(Exception ex){return new RecoveryReview(snapshot.Text,snapshot.Revision,false,ex.Message);}
        }),revision=>Task.Run(()=>
        {
            if(!string.Equals(expectedPath,workspace.ConfigPath,StringComparison.OrdinalIgnoreCase))throw new IOException("La ubicación de configuración ha cambiado.");
            workspace.AcceptRecoveredConfiguration(revision);
            workspace.RecoverPendingOperations();
        }),()=>{if(closing)return;ShowWorkspaceNotices();coordinator?.RequestReload();});
    }
    private void OpenPath(string path)
    {
        try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        catch(Exception)when(File.Exists(path))
        {var start=new ProcessStartInfo("notepad.exe"){UseShellExecute=false};start.ArgumentList.Add(path);Process.Start(start);}
    }
    private void OnSessionSwitch(SessionSwitchReason reason,int sessionId)
    {
        session.OnSessionSwitch(reason,sessionId);
        ResetContext();
    }
    private void OnPowerModeChanged(object sender,PowerModeChangedEventArgs e)
    {session.OnPowerModeChanged(e.Mode);if(e.Mode!=PowerModes.StatusChange)ResetContext();}
    private void OnDisplayChanged(object? sender,EventArgs e)=>ResetContext();
    private void OnUserPreferenceChanged(object sender,UserPreferenceChangedEventArgs e)
    {if(!closing){Post(()=>{if(!closing)tray.RefreshAppearance();});engine?.CheckExternalChanges();ResetContext();}}
    private void ResetContext()
    {
        if(closing)return;
        if(runtime!=null&&input!=null&&detector!=null)_=ObserveTask(runtime.InvokeAsync(time=>{input.SignalDiscontinuity(time.OccurredAt);detector.SetSuspended(ShouldRemainSuspended(),time.ApplyAt);}),"Entrada");
        engine?.NotifyDisplayChanged();Post(UpdateTray);
    }
    private async Task ObserveTask(Task task,string area){try{await task.ConfigureAwait(false);}catch(Exception ex){Report(ex,area);}}
    private void Report(Exception ex,string area,string? path=null)=>Post(()=>
    {
        if(closing)return;
        var configuration=ex as ConfigurationException;
        if(configuration?.Code=="ThemeApplyFailed")area="Tema";
        string code=configuration?.Code??(ex is CursorAssetException asset?asset.Code.ToString():ex.GetType().Name);
        diagnostics.Report(new(area,code,"No se ha podido completar la operación.",ex.ToString(),configuration?.SourcePath??path,configuration?.Line,configuration?.Column){PreviousConfigurationActive=area=="Configuración"?effective is not null:null});
    });
    private void Post(Action action)
    {
        if(dispatch.IsDisposed)return;
        try{dispatch.BeginInvoke(new Action(()=>{if(!dispatch.IsDisposed)action();}));}catch(InvalidOperationException){}
    }
    private async Task StopComponentsAsync(List<Exception> failures)
    {
        void Attempt(Action action){try{action();}catch(Exception ex){failures.Add(ex);}}
        async Task AwaitAttempt(Func<Task> action){try{await action();}catch(Exception ex){failures.Add(ex);}}
            Attempt(()=>source?.Dispose());source=null;
            Attempt(()=>observation?.Dispose());observation=null;
            Attempt(()=>sessionNotifications?.Dispose());sessionNotifications=null;
            if(systemEventsSubscribed)
            {
                SystemEvents.PowerModeChanged-=OnPowerModeChanged;
                SystemEvents.DisplaySettingsChanged-=OnDisplayChanged;SystemEvents.UserPreferenceChanged-=OnUserPreferenceChanged;systemEventsSubscribed=false;
            }
            if(runtime!=null&&detector!=null)await AwaitAttempt(()=>runtime.InvokeAsync(time=>detector.SetSuspended(true,time.ApplyAt)));
            if(coordinator!=null)await AwaitAttempt(()=>coordinator.DisposeAsync().AsTask());coordinator=null;
            if(engine!=null)await AwaitAttempt(async()=>
            {
                var restored=await engine.StopAsync();
                if(!restored.AllOwnedRestored)failures.Add(new IOException(string.Join(Environment.NewLine,restored.Diagnostics)));
            });
            // End the scheduler before disposing the domain object whose curve may still have a return deadline.
            Attempt(()=>runtime?.Dispose());runtime=null;
            Attempt(()=>detector?.Dispose());
            Attempt(()=>engine?.Dispose());engine=null;input=null;detector=null;engineUnavailable=true;
    }
    public void RequestMaintenanceShutdown() => Post(() =>
    {
        maintenanceShutdown=true;
        _=StopAsync();
    });
    public async Task StopAsync()
    {
        if(closing){await Stopped;return;}closing=true;
        var failures=new List<Exception>();
        void Attempt(Action action){try{action();}catch(Exception ex){failures.Add(ex);}}
        async Task AwaitAttempt(Func<Task> action){try{await action();}catch(Exception ex){failures.Add(ex);}}
        try
        {
            await AwaitAttempt(()=>initialization);
            await AwaitAttempt(()=>retryOperation);
            await AwaitAttempt(()=>diagnostics.BeginShutdownAsync());
            await StopComponentsAsync(failures);
            if(maintenanceShutdown)
            {
                try
                {
                    var pendingRecovery=RecoveryBootstrap.Inspect(options.StateDirectory);
                    if(pendingRecovery.Count>0)failures.Add(new IOException("Se conservan recibos de recuperación pendientes; no se permite retirar los recursos de la aplicación. Resuelve la recuperación al volver a abrir CursorShake. Durante este cierre la propia instancia todavía puede figurar como viva.\n"+string.Join(Environment.NewLine,pendingRecovery.Select(x=>x.DirectoryPath))));
                }
                catch(Exception error){failures.Add(error);}
            }
            if(failures.Count>0)
            {
                var diagnostic=new UiDiagnostic("Cierre","StopFailed","La restauración o el cierre necesitan revisión.",string.Join(Environment.NewLine,failures.Select(x=>x.ToString())),options.StateDirectory);
                if(options.DiagnosticPresenter!=null)options.DiagnosticPresenter(diagnostic);
                else {using var dialog=new DiagnosticDialog(diagnostic,OpenPath,language:UiLanguage);dialog.ShowDialog();}
            }
        }
        finally
        {
            Attempt(tray.Dispose);Attempt(diagnostics.Dispose);Attempt(icon.Dispose);ready.TrySetResult();
            if(failures.Count==0)stopped.TrySetResult();else stopped.TrySetException(new AggregateException(failures));
            dispatch.Dispose();ExitThread();
        }
    }
}
