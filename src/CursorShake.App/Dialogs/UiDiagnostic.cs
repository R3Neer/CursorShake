using CursorShake.Windows.Configuration;
namespace CursorShake.App.Dialogs;

public sealed record UiDiagnostic(string Area, string Code, string Summary, string Details, string? Path = null, int? Line = null, int? Column = null, IReadOnlyList<string>? RelatedPaths = null)
{
    public bool? PreviousConfigurationActive { get; init; }
    public string CopyText(AppLanguage language=AppLanguage.English)
        => $"{Area}: {Summary}\r\n{DiagnosticText.T(language,"Code","Código")}: {Code}\r\n"
            + (Path is null ? "" : $"{DiagnosticText.T(language,"Location","Ubicación")}: {Path}" + (Line is null ? "" : $":{Line}:{Column ?? 1}") + "\r\n")
            + Details
            + (RelatedPaths is { Count: > 0 } ? "\r\n"+DiagnosticText.T(language,"Preserved files","Archivos conservados")+":\r\n"+string.Join("\r\n",RelatedPaths) : "");
}
