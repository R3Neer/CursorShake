using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using CursorShake.App.Appearance;
using CursorShake.App.Tray;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Lifecycle;

namespace CursorShake.NativeSmoke;

internal static class TrayMenuRender
{
    internal static int Run(string report)
    {
        string? failure = null; bool disposed = false, startup = false, shortcutFree = false;
        TrayView? tray = null;
        string png = Path.ChangeExtension(report, ".png"), submenu = Path.ChangeExtension(report, ".submenu.png");
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            WindowsAppearance.Initialize(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            // Reading a task is side-effect free. No startup task is created, updated, run or removed by this render.
            startup = new StartupRegistration(Environment.ProcessPath!).IsEnabled();
            tray = new TrayView(SystemIcons.Application);
            var notify = (NotifyIcon)typeof(TrayView).GetField("_notify", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
            notify.Text = "CursorShake — render QA temporal";
            tray.Update(new TrayState { StartupEnabled = startup, Themes = new Dictionary<string, string> { ["windows"] = "Windows", ["bibata-ice"] = "Bibata Ice", ["nordzy-white"] = "Nordzy blanco" } });
            tray.RefreshAppearance();
            var menu = (ContextMenuStrip)typeof(TrayView).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
            shortcutFree = menu.Items.OfType<ToolStripMenuItem>().All(item => item.ShortcutKeys == Keys.None && string.IsNullOrEmpty(item.ShortcutKeyDisplayString));
            if (!shortcutFree) throw new InvalidOperationException("Real product menu unexpectedly has a keyboard shortcut.");
            menu.Items.Cast<ToolStripItem>().Single(item => item.Text == TrayText.Label(TrayActionKind.OpenConfiguration,AppLanguage.English)).Select();
            Draw(menu, png);
            Draw(menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == TrayText.ThemeTitle(AppLanguage.English,false)).DropDown, submenu);
            tray.Update(new TrayState { Language=AppLanguage.Spanish, StartupEnabled=startup, Themes=new Dictionary<string,string>{["windows"]="Windows",["bibata-ice"]="Bibata Ice",["nordzy-white"]="Nordzy White"} });
            Draw(menu,Path.ChangeExtension(report,".spanish.png"));
            Draw(menu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Text==TrayText.ThemeTitle(AppLanguage.Spanish,false)).DropDown,Path.ChangeExtension(report,".spanish.submenu.png"));
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            try { tray?.Dispose(); disposed = true; }
            catch (Exception cleanup) { failure = (failure ?? "") + Environment.NewLine + cleanup; }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, JsonSerializer.Serialize(new { Passed = failure is null && disposed && shortcutFree, Runtime = Environment.Version.ToString(), RealProductTrayView = true, TrayStateDescription = "Active Windows theme with standard bundled themes; startup check reflects the existing per-user task.", StartupEnabled = startup, StartupRegistrationWritten = false, NoInventedShortcuts = shortcutFree, OwnTemporaryNotifyIconDisposed = disposed, PopupShown = false, AppHostCreated = false, CursorEngineCreated = false, SystemCursorMutation = false, MenuImage = png, SubmenuImage = submenu, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(report); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null && disposed && shortcutFree ? 0 : 1;
    }
    private static void Draw(ToolStripDropDown menu, string file)
    {
        menu.PerformLayout(); menu.Size = menu.GetPreferredSize(Size.Empty); menu.CreateControl(); menu.PerformLayout();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(file, ImageFormat.Png);
    }
}
