using CursorShake.Windows.Configuration;
namespace CursorShake.App.Tray;

/// <summary>Tray labels only; exception messages and personal theme names remain unchanged.</summary>
public static class TrayText
{
    public static string Label(TrayActionKind kind, AppLanguage language, bool paused = false, bool hasErrors = false)
    {
        bool es = language == AppLanguage.Spanish;
        return kind switch
        {
            TrayActionKind.OpenConfiguration => es ? "Abrir configuración" : "Open configuration",
            TrayActionKind.ChooseWorkspace => es ? "Ubicación de configuración…" : "Configuration location…",
            TrayActionKind.OpenWorkspace => es ? "Abrir carpeta de configuración" : "Open configuration folder",
            TrayActionKind.Preview => es ? "Probar crecimiento" : "Preview growth",
            TrayActionKind.TogglePause => paused ? (es ? "Reanudar crecimiento" : "Resume growth") : (es ? "Pausar crecimiento" : "Pause growth"),
            TrayActionKind.ToggleStartup => es ? "Iniciar con Windows" : "Start with Windows",
            TrayActionKind.ShowErrors => hasErrors ? (es ? "Ver errores… (hay incidencias)" : "View errors… (issues reported)") : (es ? "Ver errores…" : "View errors…"),
            TrayActionKind.Exit => es ? "Salir" : "Exit",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
    public static string ThemeTitle(AppLanguage language, bool pending) => language == AppLanguage.Spanish
        ? (pending ? "Tema (cambio pendiente)" : "Tema") : (pending ? "Theme (change pending)" : "Theme");
    public static string ThemeName(string id, string supplied, AppLanguage language) => id.ToLowerInvariant() switch
    {
        "windows" => "Windows", "bibata-ice" => "Bibata Ice",
        "nordzy-white" => language == AppLanguage.Spanish ? "Nordzy blanco" : "Nordzy White", _ => supplied
    };
    public static string Status(string status, AppLanguage language)
    {
        var pair = status switch
        {
            "Activo" or "Active" => ("Active", "Activo"),
            "Pausado" or "Paused" => ("Paused", "Pausado"),
            "Cursor oculto" or "Cursor hidden" => ("Cursor hidden", "Cursor oculto"),
            "Cursor privado de la aplicación" or "Application-specific cursor" => ("Application-specific cursor", "Cursor privado de la aplicación"),
            "DPI incompatible: crecimiento pausado" or "Incompatible DPI: growth paused" => ("Incompatible DPI: growth paused", "DPI incompatible: crecimiento pausado"),
            "Cambio externo: control cedido" or "External change: control relinquished" => ("External change: control relinquished", "Cambio externo: control cedido"),
            "Motor detenido por un error" or "Engine stopped after an error" => ("Engine stopped after an error", "Motor detenido por un error"),
            "Detenido" or "Stopped" => ("Stopped", "Detenido"),
            "Iniciando…" or "Starting…" => ("Starting…", "Iniciando…"),
            "Configuración con errores" or "Configuration errors" => ("Configuration errors", "Configuración con errores"),
            "Efecto desactivado" or "Effect disabled" => ("Effect disabled", "Efecto desactivado"),
            _ => (status, status)
        };
        return language == AppLanguage.Spanish ? pair.Item2 : pair.Item1;
    }
}
