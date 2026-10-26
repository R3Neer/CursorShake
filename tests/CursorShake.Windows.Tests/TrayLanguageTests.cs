using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using CursorShake.App.Tray;
using CursorShake.Windows.Configuration;
using Xunit;
namespace CursorShake.Windows.Tests;

public sealed class TrayLanguageTests
{
    [Fact]
    public void DefaultTrayStateAndAllCommandsAreEnglish() => Sta(() =>
    {
        Assert.Equal(AppLanguage.English, new TrayState().Language);
        using var tray = new TrayView(SystemIcons.Application, visible: false);
        Assert.Equal(new[] { "Active", "Open configuration", "Configuration location…", "Open configuration folder", "Theme", "Preview growth", "Pause growth", "Start with Windows", "View errors…", "Exit" }, Items(tray).Select(x => x.Text));
        Assert.Equal("CursorShake — Active", tray.Tooltip);
    });

    [Fact]
    public void SwitchingLanguageOnSameTrayPreservesObjectsSelectionChecksAndEveryAction() => Sta(() =>
    {
        using var tray = new TrayView(SystemIcons.Application, visible: false);
        var actions = new List<TrayAction>(); tray.ActionRequested += actions.Add;
        var state = new TrayState { Paused = true, StartupEnabled = true, HasErrors = true, PendingTheme = "custom", ThemeId = "nordzy-white", StatusText = "Cambio externo: control cedido",
            Themes = new Dictionary<string, string> { ["windows"] = "Windows", ["nordzy-white"] = "Nordzy blanco", ["custom"] = "A&B" } };
        tray.Update(state);
        var originals = Items(tray); var parent = originals[4];
        var nested = parent.DropDownItems.Cast<ToolStripMenuItem>().ToArray();
        Assert.Equal("Nordzy White", nested[1].Text);
        Assert.Equal("A&&B", nested[2].Text);
        foreach (var language in new[] { AppLanguage.Spanish, AppLanguage.English, AppLanguage.Spanish })
        {
            tray.Update(state with { Language = language });
            if (language == AppLanguage.Spanish)
                Assert.Equal(new[] { "Cambio externo: control cedido", "Abrir configuración", "Ubicación de configuración…", "Abrir carpeta de configuración", "Tema (cambio pendiente)", "Probar crecimiento", "Reanudar crecimiento", "Iniciar con Windows", "Ver errores… (hay incidencias)", "Salir" }, Items(tray).Select(x => x.Text));
            Assert.Equal(originals, Items(tray));
            Assert.Equal(nested, parent.DropDownItems.Cast<ToolStripMenuItem>().ToArray());
            Assert.True(originals[6].Checked); Assert.True(originals[7].Checked);
            Assert.False(originals[0].Enabled); Assert.True(nested[1].Checked); Assert.False(nested[0].Checked);
            Assert.Equal(language == AppLanguage.Spanish ? "Reanudar crecimiento" : "Resume growth", originals[6].Text);
            Assert.Equal(language == AppLanguage.Spanish ? "Tema (cambio pendiente)" : "Theme (change pending)", parent.Text);
            Assert.Equal(language == AppLanguage.Spanish ? "Ver errores… (hay incidencias)" : "View errors… (issues reported)", originals[8].Text);
            Assert.Equal("CursorShake — " + TrayText.Status(state.StatusText, language), tray.Tooltip);
        }
        foreach (int index in new[] { 1, 2, 3, 5, 6, 7, 8, 9 }) originals[index].PerformClick();
        nested[2].PerformClick();
        Assert.Equal(new[] { TrayActionKind.OpenConfiguration, TrayActionKind.ChooseWorkspace, TrayActionKind.OpenWorkspace, TrayActionKind.Preview, TrayActionKind.TogglePause, TrayActionKind.ToggleStartup, TrayActionKind.ShowErrors, TrayActionKind.Exit, TrayActionKind.SelectTheme }, actions.Select(x => x.Kind));
        Assert.Equal("custom", actions[^1].ThemeId);
    });

    [Theory]
    [InlineData("Activo", "Active")]
    [InlineData("Pausado", "Paused")]
    [InlineData("Cursor oculto", "Cursor hidden")]
    [InlineData("Cursor privado de la aplicación", "Application-specific cursor")]
    [InlineData("DPI incompatible: crecimiento pausado", "Incompatible DPI: growth paused")]
    [InlineData("Cambio externo: control cedido", "External change: control relinquished")]
    [InlineData("Motor detenido por un error", "Engine stopped after an error")]
    [InlineData("Detenido", "Stopped")]
    [InlineData("Iniciando…", "Starting…")]
    [InlineData("Configuración con errores", "Configuration errors")]
    [InlineData("Efecto desactivado", "Effect disabled")]
    public void KnownStatusesTranslateBothDirections(string spanish, string english)
    {
        Assert.Equal(english, TrayText.Status(spanish, AppLanguage.English));
        Assert.Equal(spanish, TrayText.Status(english, AppLanguage.Spanish));
    }

    [Fact]
    public void CustomStatusIsPreservedAndTooltipTruncationDoesNotSplitSurrogate() => Sta(() =>
    {
        using var tray = new TrayView(SystemIcons.Application, visible: false);
        string status = new string('x', 48) + "😀 custom status";
        tray.Update(new TrayState { StatusText = status });
        Assert.Equal(status, Items(tray)[0].Text);
        Assert.True(tray.Tooltip.Length <= 63);
        Assert.False(char.IsHighSurrogate(tray.Tooltip[^1]));
    });

    private static ToolStripMenuItem[] Items(TrayView tray) => tray.Menu.Items.OfType<ToolStripMenuItem>().ToArray();
    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI fixture timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}

