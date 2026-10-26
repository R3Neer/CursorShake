using System.Drawing.Imaging;
using System.Text.Json;
using CursorShake.App.Appearance;

namespace CursorShake.NativeSmoke;

internal static class MenuRender
{
    internal static int Run(string output)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        WindowsAppearance.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        WindowsAppearance.Refresh();
        using var menu = new ContextMenuStrip();
        menu.Items.AddRange([new ToolStripMenuItem("Abrir configuración"), new ToolStripMenuItem("Pausar") { Checked = true }, new ToolStripMenuItem("Vista previa no disponible") { Enabled = false }, new ToolStripSeparator(), new ToolStripMenuItem("Tema", null, new ToolStripMenuItem("Windows") { Checked = true }, new ToolStripMenuItem("Bibata Ice"), new ToolStripMenuItem("Nordzy blanco")), new ToolStripMenuItem("Iniciar con Windows") { Checked = true }, new ToolStripSeparator(), new ToolStripMenuItem("Salir")]);
        WindowsAppearance.StyleMenu(menu);
        menu.PerformLayout(); menu.Size = menu.GetPreferredSize(Size.Empty); menu.CreateControl(); menu.PerformLayout();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(output, ImageFormat.Png);
        File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(new { Runtime = Environment.Version.ToString(), ApplicationColorMode = Application.ColorMode.ToString(), SystemColorMode = Application.SystemColorMode.ToString(), SystemInformation.HighContrast, Background = menu.BackColor.ToArgb(), Foreground = menu.ForeColor.ToArgb(), menu.DeviceDpi, menu.Width, menu.Height, PopupShown = false, NotifyIconCreated = false, SystemCursorMutation = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        return 0;
    }
}
