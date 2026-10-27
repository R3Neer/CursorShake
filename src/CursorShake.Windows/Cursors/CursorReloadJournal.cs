using CursorShake.Windows.Configuration;
namespace CursorShake.Windows.Cursors;

/// <summary>One bounded local incident record. A successful recovery never opens an error popup.</summary>
internal sealed class CursorReloadJournal(string stateDirectory)
{
    internal const string FileName = "cursor-reload.json";
    private readonly string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateDirectory))!, FileName);
    private Incident? incident;
    internal sealed record Incident(DateTimeOffset ObservedUtc, string Diagnostic, string Outcome, string? Detail, DateTimeOffset UpdatedUtc);
    internal void Detected(string? diagnostic)
    {
        var now = DateTimeOffset.UtcNow;
        incident = new(now, diagnostic ?? "The first native divergence was not available.", "Detected", null, now);
        Save();
    }
    internal void Completed(string outcome, string detail)
    {
        if(incident is null)return;
        incident=incident with { Outcome=outcome, Detail=detail, UpdatedUtc=DateTimeOffset.UtcNow };
        Save();
    }
    private void Save()
    {
        try { DurableState.Write(path,incident); }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ConfigurationException)
        { System.Diagnostics.Debug.WriteLine("Cursor reload diagnostics could not be saved: "+error.Message); }
    }
}