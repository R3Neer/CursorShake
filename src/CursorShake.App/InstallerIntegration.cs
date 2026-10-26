namespace CursorShake.App;

internal enum InstallerAction { NormalLaunch, InstallerLaunch, EnableStartup, DisableStartup, Invalid }
internal sealed record InstallerResult(bool StartApplication, int ExitCode, Exception? Error = null);

/// <summary>Private installer entry points; never create a host, workspace, or cursor engine.</summary>
internal static class InstallerIntegration
{
    internal static InstallerAction Parse(string[] arguments) => arguments switch
    {
        [] => InstallerAction.NormalLaunch,
        ["--installer-launch"] => InstallerAction.InstallerLaunch,
        ["--installer-startup=enable"] => InstallerAction.EnableStartup,
        ["--installer-startup=disable"] => InstallerAction.DisableStartup,
        _ => InstallerAction.Invalid
    };

    // Exit codes: 0 success, 1 operation failed, 2 invalid invocation, 3 unsafe execution context.
    internal static InstallerResult Execute(InstallerAction action, bool interactiveUser, bool elevated, Action<bool> configureStartup)
    {
        if (action == InstallerAction.Invalid) return new(false, 2);
        if (action == InstallerAction.NormalLaunch) return new(true, 0);
        if (!interactiveUser || elevated)
            return new(false, 3, new InvalidOperationException(
                "La integración del instalador necesita una sesión de usuario sin elevar. " +
                "No se ha cambiado el inicio automático ni iniciado el motor. Abre CursorShake desde tu sesión normal " +
                "y usa «Iniciar con Windows» en la bandeja. Abre el instalador normalmente; este solicitará elevarse si eliges instalar para todos."));
        if (action == InstallerAction.InstallerLaunch) return new(true, 0);
        if (action is not (InstallerAction.EnableStartup or InstallerAction.DisableStartup)) return new(false, 2);
        try
        {
            configureStartup(action == InstallerAction.EnableStartup);
            return new(false, 0);
        }
        catch (Exception error) { return new(false, 1, error); }
    }
}
