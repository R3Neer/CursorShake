using CursorShake.Windows.Configuration;
namespace CursorShake.App.Tray;

public enum TrayActionKind
{
    OpenConfiguration, ChooseWorkspace, OpenWorkspace, SelectTheme, Preview,
    TogglePause, ToggleStartup, ShowErrors, Exit
}
public sealed record TrayAction(TrayActionKind Kind, string? ThemeId = null);
public sealed record TrayState
{
    public AppLanguage Language { get; init; } = AppLanguage.English;
    public string ThemeId { get; init; } = "windows";
    public bool Paused { get; init; }
    public bool StartupEnabled { get; init; }
    public bool HasErrors { get; init; }
    public string? PendingTheme { get; init; }
    public string StatusText { get; init; } = "Active";
    public IReadOnlyDictionary<string, string> Themes { get; init; } = new Dictionary<string, string>();
}
