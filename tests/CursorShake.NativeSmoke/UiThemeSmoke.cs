using System.Drawing.Imaging;
using System.Text.Json;
using CursorShake.App.Appearance;

namespace CursorShake.NativeSmoke;

internal static class UiThemeSmoke
{
    internal static int Run(string report)
    {
        string? failure = null;
        string? initial = null, forced = null, restored = null, osTheme = null;
        bool highContrast = false, selected = false, nestedStyled = false;
        string png = Path.ChangeExtension(report, ".png");
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            WindowsAppearance.Initialize();
            initial = Application.ColorMode.ToString();
            Need(Application.ColorMode == SystemColorMode.System, "Initialize must select the application System color mode before UI creation.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            osTheme = Application.SystemColorMode.ToString(); highContrast = SystemInformation.HighContrast;
            using var menu = new ContextMenuStrip();
            var pause = new ToolStripMenuItem("Pausar") { Checked = true };
            var themes = new ToolStripMenuItem("Tema");
            themes.DropDownItems.Add(new ToolStripMenuItem("Windows") { Checked = true });
            themes.DropDownItems.Add("Bibata Ice"); themes.DropDownItems.Add("Nordzy blanco");
            menu.Items.AddRange([new ToolStripMenuItem("Abrir configuración"), pause, new ToolStripMenuItem("Vista previa no disponible") { Enabled = false }, new ToolStripSeparator(), themes, new ToolStripMenuItem("Iniciar con Windows") { Checked = true }, new ToolStripSeparator(), new ToolStripMenuItem("Salir")]);
            WindowsAppearance.StyleMenu(menu);
            var expected = highContrast ? SystemColorMode.Classic : Application.SystemColorMode;
            Need(Application.ColorMode == expected, "Styling must follow the effective Windows appearance.");
            var opposite = expected == SystemColorMode.Dark ? SystemColorMode.Classic : SystemColorMode.Dark;
            Application.SetColorMode(opposite); forced = Application.ColorMode.ToString();
            Need(Application.ColorMode == opposite, "The fixture must change only this process to the opposite color mode.");
            WindowsAppearance.Refresh(); restored = Application.ColorMode.ToString();
            Need(Application.ColorMode == expected, "Refresh must restore this process to the effective Windows appearance.");
            WindowsAppearance.StyleMenu(menu);
            nestedStyled = menu.BackColor == themes.DropDown.BackColor && menu.ForeColor == themes.DropDown.ForeColor;
            Need(nestedStyled, "Nested menus must follow the same effective colors.");
            if (highContrast) Need(menu.Renderer is ToolStripSystemRenderer, "High contrast must use the system renderer.");
            else
            {
                bool dark = Application.SystemColorMode == SystemColorMode.Dark;
                Need(dark ? menu.BackColor.GetBrightness() < .3f && menu.ForeColor.GetBrightness() > .8f : menu.BackColor.GetBrightness() > .8f && menu.ForeColor.GetBrightness() < .3f, "Effective menu text and surface must remain legible.");
            }
            menu.PerformLayout(); menu.Size = menu.GetPreferredSize(Size.Empty); menu.CreateControl(); menu.PerformLayout();
            pause.Select(); selected = pause.Selected;
            Need(selected, "Fixture item must be selected to exercise rounded hover rendering.");
            using var bitmap = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            bitmap.Save(png, ImageFormat.Png);
        }
        catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, JsonSerializer.Serialize(new { Passed = failure is null, Runtime = Environment.Version.ToString(), InitialApplicationMode = initial, OsTheme = osTheme, HighContrast = highContrast, ForcedProcessMode = forced, RestoredApplicationMode = restored, NestedStyled = nestedStyled, SelectedItem = selected, SelectedMenuPng = png, NoGlobalThemeChanges = true, WindowsSettingsWritten = false, NotifyIconCreated = false, AppHostCreated = false, PopupShown = false, SystemCursorMutation = false, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(report); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
