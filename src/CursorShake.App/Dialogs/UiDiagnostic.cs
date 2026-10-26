namespace CursorShake.App.Dialogs;

public sealed record UiDiagnostic(string Area, string Code, string Summary, string Details, string? Path = null, int? Line = null, int? Column = null, IReadOnlyList<string>? RelatedPaths = null)
{
    public string CopyText() => $"{Area}: {Summary}\r\nCódigo: {Code}\r\n" + (Path is null ? "" : $"Ubicación: {Path}" + (Line is null ? "" : $":{Line}:{Column ?? 1}") + "\r\n") + Details + (RelatedPaths is { Count: > 0 } ? "\r\nArchivos conservados:\r\n" + string.Join("\r\n", RelatedPaths) : "");
}
