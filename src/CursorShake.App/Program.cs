using System.Security.Principal;
using System.Diagnostics;
using CursorShake.App.Dialogs;
using CursorShake.App.Appearance;

namespace CursorShake.App;
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        WindowsAppearance.Initialize();
        string sid=WindowsIdentity.GetCurrent().User?.Value??Environment.UserName;
        using var instance=new Mutex(false,$"Local\\CursorShake.App.{sid}.{Process.GetCurrentProcess().SessionId}");
        bool owns;
        try{owns=instance.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
        if(!owns)return;
        try{using var context=new AppHost();Application.Run(context);}
        catch(Exception ex)
        {
            using var dialog=new DiagnosticDialog(new("Inicio","StartupFailed","CursorShake no ha podido iniciarse.",ex.ToString()));
            dialog.ShowDialog();
        }
        finally{instance.ReleaseMutex();}
    }
}
