using CursorShake.Windows.Configuration;
namespace CursorShake.App.Dialogs;

public enum DiagnosticFileAction { None, OpenConfiguration, OpenFolder }
public sealed record DiagnosticPresentation(string Title,string Explanation,string Guidance,bool Warning,bool CanRetry,string? FileActionLabel,DiagnosticFileAction FileAction=DiagnosticFileAction.None);

/// <summary>User-facing facts and actions only. Technical evidence stays in the separate diagnostic details.</summary>
public static class DiagnosticText
{
    public static string T(AppLanguage language,string english,string spanish)=>language==AppLanguage.Spanish?spanish:english;
    public static DiagnosticPresentation Present(UiDiagnostic diagnostic,AppLanguage language=AppLanguage.English)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var area=Area(diagnostic.Area);bool configurationProblem=false;
        DiagnosticPresentation Message(string title,string titleEs,string explanation,string explanationEs,string guidance,string guidanceEs,bool warning=false,bool retry=false,bool config=false)
        {
            configurationProblem=config;
            return new(T(language,title,titleEs),T(language,explanation,explanationEs),T(language,guidance,guidanceEs),warning,retry,null);
        }
        var presentation=diagnostic.Code switch
        {
            "InvalidToml"=>Message("Configuration could not be read","No se ha podido leer la configuración",
                "The configuration file contains a formatting error.","El archivo de configuración contiene un error de formato.",
                "Open configuration, correct the reported line, and save the file.","Abre la configuración, corrige la línea indicada y guarda el archivo.",config:true),
            "InvalidConfiguration"=>Message("Configuration could not be applied","No se ha podido aplicar la configuración",
                "A configuration setting is missing or has an unsupported value.","Falta un ajuste o uno de sus valores no es válido.",
                "Open configuration, review the reported setting, and save the file.","Abre la configuración, revisa el ajuste indicado y guarda el archivo.",config:true),
            "UnsupportedVersion"=>Message("Configuration version is not supported","La versión de configuración no es compatible",
                "This version of CursorShake cannot read that configuration version.","Esta versión de CursorShake no puede leer esa versión de configuración.",
                "Review the configuration version before editing it, or use a compatible CursorShake version.","Revisa la versión de la configuración antes de editarla o usa una versión compatible de CursorShake.",config:true),
            "LimitExceeded"=>Message("A supported limit was exceeded","Se ha superado un límite compatible",
                "This operation exceeds a supported size or resource limit.","La operación supera un límite de tamaño o de recursos.",
                "Review the limit in technical details. Use smaller cursor resources or reduce the size setting when applicable.","Revisa el límite en los detalles técnicos. Si corresponde, usa recursos de cursor más pequeños o reduce el tamaño configurado.",config:area==OperationArea.Configuration),
            "DestinationConflict"=>Message("Configuration was not moved","No se ha trasladado la configuración",
                "The selected folder already contains configuration or personal themes.","La carpeta elegida ya contiene una configuración o temas personales.",
                "Choose another folder from Configuration location in the tray menu.","Elige otra carpeta desde Ubicación de configuración en la bandeja."),
            "InvalidDestination"=>Message("That folder cannot be used","No se puede usar esa carpeta",
                "The selected destination is not suitable for the configuration and personal themes.","El destino elegido no es adecuado para la configuración y los temas personales.",
                "Choose a different destination from Configuration location in the tray menu.","Elige otro destino desde Ubicación de configuración en la bandeja."),
            "UnsafePath"=>Message("This location cannot be used safely","No se puede usar esta ubicación con seguridad",
                "CursorShake could not verify the selected file or folder.","CursorShake no ha podido verificar el archivo o la carpeta elegidos.",
                "Choose another location. Review technical details if you need to identify the affected file.","Elige otra ubicación. Consulta los detalles técnicos para identificar el archivo afectado."),
            "Busy"=>Message("A required file is in use","Un archivo necesario está en uso",
                "Another operation is using a file CursorShake needs.","Otra operación está usando un archivo que CursorShake necesita.",
                "Wait for that operation to finish, then try again.","Espera a que termine esa operación y vuelve a intentarlo.",warning:true,retry:area!=OperationArea.Shutdown,config:area==OperationArea.Configuration),
            "Conflict"=>Message("The operation needs another review","La operación necesita otra revisión",
                "The relevant files or settings changed before the operation finished.","Los archivos o ajustes han cambiado antes de que terminara la operación.",
                "Review the current files before trying again. Do not overwrite a version you want to keep.","Revisa los archivos actuales antes de volver a intentarlo. No sobrescribas una versión que quieras conservar.",warning:true,config:area==OperationArea.Configuration),
            "RecoveryRequired"=>Message("Recovery needs your attention","La recuperación necesita tu atención",
                "CursorShake found unfinished recovery work and cannot safely continue this operation.","CursorShake ha encontrado una recuperación pendiente y no puede continuar esta operación con seguridad.",
                "Review the available recovery options. Keep the preserved files until the issue is resolved.","Revisa las opciones de recuperación disponibles. Conserva los archivos guardados hasta resolver el problema.",warning:true),
            "ThemeApplyFailed"=>Message("The cursor theme could not be applied","No se ha podido aplicar el tema de cursor",
                "CursorShake could not activate the selected cursor theme.","CursorShake no ha podido activar el tema de cursor elegido.",
                "Choose another theme from Theme in the tray menu. For a personal theme, check its cursor files.","Elige otro tema desde Tema en la bandeja. Si usas uno personal, comprueba sus archivos de cursor.",retry:true),
            "CursorOwnershipLost"=>Message("The cursor effect is paused","El efecto del cursor está pausado",
                "The pointer no longer matches the state CursorShake was using, so it stopped changing the cursor.","El puntero ya no coincide con el estado que estaba usando CursorShake y se ha pausado el efecto.",
                "The current cursors are being left in place. You can keep using them and close CursorShake to keep the effect stopped.","Se conservan los cursores actuales. Puedes seguir usándolos y cerrar CursorShake para que el efecto permanezca detenido.",warning:true),
            "StopFailed"=>Message("Cursor restoration needs attention","La restauración del cursor necesita atención",
                "CursorShake could not verify a clean restoration and shutdown.","CursorShake no ha podido verificar que la restauración y el cierre hayan terminado correctamente.",
                "Review the recovery details before starting another cursor utility.","Revisa los detalles de recuperación antes de iniciar otra utilidad de cursores.",warning:true),
            "StartupRegistrationFailed" or "StartupConflict" or "ExecutableMissing"=>StartupSettings(),
            _=>area switch
            {
                OperationArea.StartupSettings=>StartupSettings(),
                OperationArea.Initialization=>Message("CursorShake could not start","CursorShake no ha podido iniciarse",
                    "The cursor effect is not ready to run.","El efecto del cursor no está preparado para funcionar.",
                    "Try again. If the problem returns, open technical details to review what happened.","Vuelve a intentarlo. Si el problema se repite, abre los detalles técnicos para revisar lo ocurrido.",retry:true),
                OperationArea.Configuration=>Message("The configuration change could not be completed","No se ha podido completar el cambio de configuración",
                    "CursorShake could not finish this operation.","CursorShake no ha podido terminar esta operación.",
                    "Copy the technical information if you ask for help. You can close this message.","Copia la información técnica si vas a pedir ayuda. Puedes cerrar este mensaje.",config:true),
                OperationArea.Cursor=>Message("The cursor operation could not be completed","No se ha podido completar la operación del cursor",
                    "CursorShake could not finish this operation.","CursorShake no ha podido terminar esta operación.",
                    "Copy the technical information if you ask for help. You can close this message.","Copia la información técnica si vas a pedir ayuda. Puedes cerrar este mensaje."),
                OperationArea.Recovery=>Message("Recovery could not be completed","No se ha podido completar la recuperación",
                    "CursorShake could not verify that this recovery operation finished safely.","CursorShake no ha podido verificar que esta recuperación haya terminado con seguridad.",
                    "Review the technical details. Keep the preserved files until the issue is resolved.","Revisa los detalles técnicos. Conserva los archivos guardados hasta resolver el problema.",warning:true),
                _=>Message("The operation could not be completed","No se ha podido completar la operación",
                    "CursorShake could not finish this operation.","CursorShake no ha podido terminar esta operación.",
                    "Copy the technical information if you ask for help. You can close this message.","Copia la información técnica si vas a pedir ayuda. Puedes cerrar este mensaje.")
            }
        };
        if(diagnostic.Code is "InvalidToml" or "InvalidConfiguration" && diagnostic.Line is >0)
        {
            string line=diagnostic.Line.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            presentation=presentation with{Guidance=T(language,$"Open configuration, review line {line}, and save the file.",$"Abre la configuración, revisa la línea {line} y guarda el archivo.")};
        }
        if(diagnostic.PreviousConfigurationActive is {} previous)
            presentation=presentation with{Explanation=presentation.Explanation+" "+(previous
                ?T(language,"Your previous configuration remains active.","Tu configuración anterior sigue activa.")
                :T(language,"There was no previous configuration to keep.","No había una configuración anterior que conservar."))};
        DiagnosticFileAction fileAction=diagnostic.Path is null||diagnostic.Code is "ThemeApplyFailed" or "CursorOwnershipLost"
            ?DiagnosticFileAction.None
            :configurationProblem&&diagnostic.Path.EndsWith(".toml",StringComparison.OrdinalIgnoreCase)?DiagnosticFileAction.OpenConfiguration:DiagnosticFileAction.OpenFolder;
        string? fileLabel=fileAction switch
        {
            DiagnosticFileAction.OpenConfiguration=>T(language,"Edit","Editar"),
            DiagnosticFileAction.OpenFolder=>T(language,"Open folder","Abrir carpeta"),
            _=>null
        };
        return presentation with{Warning=presentation.Warning||(configurationProblem&&diagnostic.PreviousConfigurationActive==true),FileActionLabel=fileLabel,FileAction=fileAction};

        DiagnosticPresentation StartupSettings()=>Message("Startup settings could not be changed","No se ha podido cambiar el inicio con Windows",
            "The requested startup preference could not be applied.","No se ha podido aplicar la preferencia de inicio solicitada.",
            "Use Start with Windows in the tray menu to try again.","Usa Iniciar con Windows en la bandeja para volver a intentarlo.");
    }
    private enum OperationArea{Other,Configuration,Cursor,Initialization,StartupSettings,Recovery,Shutdown}
    private static OperationArea Area(string area)=>area.Trim().ToLowerInvariant() switch
    {
        "configuración" or "configuration"=>OperationArea.Configuration,
        "cursor" or "entrada" or "input"=>OperationArea.Cursor,
        "inicio" or "startup" or "initialization"=>OperationArea.Initialization,
        "inicio con windows" or "start with windows" or "startup settings"=>OperationArea.StartupSettings,
        "recuperación" or "recovery"=>OperationArea.Recovery,
        "cierre" or "shutdown"=>OperationArea.Shutdown,
        _=>OperationArea.Other
    };
}
