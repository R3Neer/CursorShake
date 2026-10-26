using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Forms;
using CursorShake.App.Appearance;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class WindowsAppearanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitThemeStylesNestedMenusWithoutChangingTheirCommandsOrState(bool dark) => Sta(() =>
    {
        using var menu = NewMenu(out var checkedItem, out var disabledItem, out var parent);
        int clicks = 0; checkedItem.Click += (_, _) => clicks++;
        var names = Flatten(menu).Select(item => item.Text).ToArray();
        WindowsAppearance.StyleMenu(menu, dark, false);
        Assert.Equal(names, Flatten(menu).Select(item => item.Text));
        Assert.True(checkedItem.Checked);
        Assert.True(checkedItem.Enabled);
        Assert.False(disabledItem.Enabled);
        Assert.Equal(Keys.Control | Keys.P, checkedItem.ShortcutKeys);
        Assert.True(checkedItem.CheckOnClick);
        Assert.True(dark ? menu.BackColor.GetBrightness() < .3f : menu.BackColor.GetBrightness() > .8f);
        Assert.True(dark ? menu.ForeColor.GetBrightness() > .8f : menu.ForeColor.GetBrightness() < .3f);
        Assert.All(new[] { parent.DropDown, ((ToolStripMenuItem)parent.DropDownItems[2]).DropDown }, nested =>
        {
            Assert.Equal(menu.BackColor, nested.BackColor);
            Assert.Equal(menu.ForeColor, nested.ForeColor);
            Assert.IsAssignableFrom<ToolStripProfessionalRenderer>(nested.Renderer);
        });
        checkedItem.PerformClick();
        Assert.Equal(1, clicks);
        Assert.False(checkedItem.Checked); // Existing toggle semantics survive styling.
    });

    [Fact]
    public void ReapplyingOrSwitchingAppearanceDoesNotAccumulatePaddingOrChangeState() => Sta(() =>
    {
        using var menu = NewMenu(out var checkedItem, out var disabled, out _);
        WindowsAppearance.StyleMenu(menu, false, false);
        var padding = Flatten(menu).Select(item => (item.Padding, item.Margin)).ToArray();
        var rootPadding = menu.Padding;
        for (int i = 0; i < 20; i++) WindowsAppearance.StyleMenu(menu, i % 2 == 0, false);
        Assert.Equal(padding, Flatten(menu).Select(item => (item.Padding, item.Margin)).ToArray());
        Assert.Equal(rootPadding, menu.Padding);
        Assert.True(checkedItem.Checked);
        Assert.False(disabled.Enabled);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HighContrastUsesSystemRendererAndSystemColorsForEverySubmenu(bool dark) => Sta(() =>
    {
        using var menu = NewMenu(out var checkedItem, out var disabled, out var themes);
        WindowsAppearance.StyleMenu(menu, dark, true);
        foreach (var popup in new[] { menu, themes.DropDown, ((ToolStripMenuItem)themes.DropDownItems[2]).DropDown })
        {
            Assert.IsType<ToolStripSystemRenderer>(popup.Renderer);
            Assert.Equal(SystemColors.Menu, popup.BackColor);
            Assert.Equal(SystemColors.MenuText, popup.ForeColor);
        }
        Assert.True(checkedItem.Checked);
        Assert.False(disabled.Enabled);
    });

    [Fact]
    public void RealDetachedContextMenusRenderLightDarkAndHighContrastFixtures() => Sta(() =>
    {
        string output = Path.Combine(RepositoryRoot(), "artifacts", "tests", "ui-theme");
        Directory.CreateDirectory(output);
        foreach (var variant in new[] { (Name: "light", Dark: false, HighContrast: false), (Name: "dark", Dark: true, HighContrast: false), (Name: "high-contrast", Dark: false, HighContrast: true) })
        {
            using var menu = NewMenu(out _, out _, out var themes);
            WindowsAppearance.StyleMenu(menu, variant.Dark, variant.HighContrast);
            Render(menu, Path.Combine(output, variant.Name + "-menu.png"));
            menu.Items[1].Select();
            Assert.True(menu.Items[1].Selected);
            Render(menu, Path.Combine(output, variant.Name + "-selected-menu.png"));
            Render(themes.DropDown, Path.Combine(output, variant.Name + "-submenu.png"));
        }
        File.WriteAllText(Path.Combine(output, "fixture-notes.txt"), "Detached real ContextMenuStrip/ToolStripDropDown DrawToBitmap fixtures. No popup was shown, no NotifyIcon/AppHost was created, no desktop or cursor was changed. Explicit dark/high-contrast parameters avoid changing process or Windows theme settings. Comando de prueba/Ctrl+P is deliberately synthetic to verify shortcut preservation; it is not a CursorShake command. Use real-product-menu.png for product presentation.");
    });

    private static void Render(ToolStripDropDown menu, string path)
    {
        menu.PerformLayout();
        menu.Size = menu.GetPreferredSize(Size.Empty);
        menu.CreateControl();
        menu.PerformLayout();
        Assert.InRange(menu.Width, 100, 1200);
        Assert.InRange(menu.Height, 40, 1200);
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
        Assert.True(new FileInfo(path).Length > 1000, "Menu fixture must include rendered content.");
        int different = 0;
        for (int y = 3; y < bitmap.Height - 3; y++) for (int x = 3; x < bitmap.Width - 3; x++)
            if (bitmap.GetPixel(x, y).ToArgb() != menu.BackColor.ToArgb()) different++;
        Assert.True(different > 100, "Fixture should contain text, checks, separators and arrows.");
        File.WriteAllText(path + ".json", JsonSerializer.Serialize(new { menu.Width, menu.Height, menu.DeviceDpi, Background = menu.BackColor.ToArgb(), Foreground = menu.ForeColor.ToArgb(), Items = menu.Items.Cast<ToolStripItem>().Select(item => new { item.Text, item.Bounds, item.Enabled, item.Selected, Checked = item is ToolStripMenuItem command && command.Checked }) }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static ContextMenuStrip NewMenu(out ToolStripMenuItem selected, out ToolStripMenuItem disabled, out ToolStripMenuItem themes)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = true };
        selected = new("Comando de prueba") { Checked = true, CheckOnClick = true, ShortcutKeys = Keys.Control | Keys.P };
        disabled = new("Vista previa no disponible") { Enabled = false };
        themes = new("Tema");
        themes.DropDownItems.Add(new ToolStripMenuItem("Windows") { Checked = true });
        themes.DropDownItems.Add("Bibata Ice");
        var more = new ToolStripMenuItem("Más temas"); more.DropDownItems.Add("Nordzy blanco"); themes.DropDownItems.Add(more);
        menu.Items.AddRange([new ToolStripMenuItem("Abrir configuración"), selected, disabled, new ToolStripSeparator(), themes, new ToolStripMenuItem("Iniciar con Windows") { Checked = true }, new ToolStripSeparator(), new ToolStripMenuItem("Salir")]);
        return menu;
    }
    private static IEnumerable<ToolStripItem> Flatten(ToolStripDropDown menu) => menu.Items.Cast<ToolStripItem>().SelectMany(item => item is ToolStripMenuItem command && command.HasDropDownItems ? new[] { item }.Concat(Flatten(command.DropDown)) : [item]);
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "CursorShake.Windows.Tests"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Appearance fixture STA did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
