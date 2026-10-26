using System.Security.Principal;
using System.Diagnostics;
using CursorShake.App.Dialogs;
using CursorShake.App.Appearance;
using CursorShake.Windows.Lifecycle;

namespace CursorShake.App;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Maintenance runs before the instance mutex, so an existing tray instance cannot swallow it.
        if(InstallerMaintenance.Parse(args) is {} maintenance)
        {
            try
            {
                if(maintenance.Action==MaintenanceAction.UninstallRemoveData && Environment.UserInteractive)
                    InitializeAppearance();
                InstallerMaintenance.Execute(maintenance);return 0;
            }
            catch(OperationCanceledException){return 1223;}
            catch(Exception error)
            {
                if(Environment.UserInteractive && Process.GetCurrentProcess().SessionId!=0)
                {InitializeAppearance();ShowFailure("Mantenimiento","MaintenanceFailed",error);}
                return 1;
            }
        }
        var action=InstallerIntegration.Parse(args);
        if(action==InstallerAction.Invalid)return 2;
        using var process=Process.GetCurrentProcess();
        using var identity=WindowsIdentity.GetCurrent();
        bool interactiveUser=Environment.UserInteractive && process.SessionId!=0;
        var result=InstallerIntegration.Execute(action,interactiveUser,
            new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            enabled=>new StartupRegistration(Environment.ProcessPath??throw new InvalidOperationException("Executable path is unavailable.")).SetEnabled(enabled));
        if(!result.StartApplication)
        {
            if(result.Error is not null && interactiveUser)
            {
                InitializeAppearance();
                ShowFailure("Integración del instalador","InstallerIntegrationFailed",result.Error);
            }
            return result.ExitCode;
        }
        InitializeAppearance();
        string sid=identity.User?.Value??Environment.UserName;
        using var instance=new Mutex(false,$"Local\\CursorShake.App.{sid}.{process.SessionId}");
        bool owns;
        try{owns=instance.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
        if(!owns)return 0;
        try
        {
            AppHost? context=null;
            MaintenanceEndpoint endpoint;
            using(InstallationGate.TryEnter(AppContext.BaseDirectory,TimeSpan.Zero))
            {
                endpoint=new MaintenanceEndpoint(()=>context?.RequestMaintenanceShutdown());
                try
                {
                    InstallationRecoveryIndex.RegisterCurrentUser(AppContext.BaseDirectory,
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CursorShake","state"));
                    context=new AppHost();
                }
                catch{endpoint.Dispose();throw;}
            }
            using(context)
            using(endpoint)
            {
                Application.Run(context);
                bool clean=context.Stopped.IsCompletedSuccessfully;
                _=context.Stopped.Exception;
                return clean?0:1;
            }
        }
        catch(InstallationBusyException){return 4;}
        catch(Exception ex)
        {
            ShowFailure("Inicio","StartupFailed",ex);
            return 1;
        }
        finally{instance.ReleaseMutex();}
    }
    private static bool appearanceInitialized;
    private static void InitializeAppearance()
    {
        if(appearanceInitialized)return;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        WindowsAppearance.Initialize();
        appearanceInitialized=true;
    }
    private static void ShowFailure(string area,string code,Exception error)
    {
        using var dialog=new DiagnosticDialog(new(area,code,"CursorShake no ha podido completar la operación.",error.ToString()));
        dialog.ShowDialog();
    }
}
