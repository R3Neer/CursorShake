using CursorShake.Core.Shake;
using CursorShake.Windows.Cursors;
namespace CursorShake.Windows.Configuration;

public sealed record AppConfiguration
{
    public int Version { get; init; } = 1;
    public string Theme { get; init; } = "windows";
    public GestureProfile Gesture { get; init; } = new();
    public IReadOnlyDictionary<string, PersonalThemeDefinition> PersonalThemes { get; init; } = new Dictionary<string, PersonalThemeDefinition>(StringComparer.OrdinalIgnoreCase);
}
public sealed record PersonalThemeDefinition
{
    public string? DefaultCursor { get; init; }
    public IReadOnlyDictionary<CursorRole, string> Roles { get; init; } = new Dictionary<CursorRole, string>();
}
public sealed class ConfigurationException : Exception
{
    public string Code { get; }
    public string? SourcePath { get; }
    public int? Line { get; }
    public int? Column { get; }
    public ConfigurationException(string code, string message, string? sourcePath = null, int? line = null, int? column = null, Exception? inner = null)
        : base(message + (sourcePath is null ? "" : $"\n{sourcePath}" + (line is null ? "" : $":{line}:{column ?? 1}")), inner)
    { Code = code; SourcePath = sourcePath; Line = line; Column = column; }
}
public sealed record ConfigurationSnapshot(string Text, string Revision);
public sealed record WorkspaceDiagnostic(string Code, string Message, string Path, IReadOnlyList<string>? RelatedPaths = null);
public sealed record WorkspaceMoveResult(string RootPath, bool CleanupPending);
public enum WorkspaceCheckpoint { WriteJournalDurable, WriteStarted, WriteFlushed, MoveCopied, MovePrepared, LocatorWrittenBeforeFlush, RootCommitted, CleanupStarted }
