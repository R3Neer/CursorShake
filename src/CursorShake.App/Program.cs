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
        try{using var context=new AppHost();Application.Run(context);}
        catch(Exception ex)
        {
            ShowFailure("Inicio","StartupFailed",ex);
            return 1;
        }
        finally{instance.ReleaseMutex();}
        return 0;
    }
    private static void InitializeAppearance()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        WindowsAppearance.Initialize();
    }
    private static void ShowFailure(string area,string code,Exception error)
    {
        using var dialog=new DiagnosticDialog(new(area,code,"CursorShake no ha podido completar la operación.",error.ToString()));
        dialog.ShowDialog();
    }
}
