using System.Text;
using System.Text.Json;
using Microsoft.Win32;
namespace CursorShake.Windows.Configuration;

internal sealed record WorkspaceLocator(string Root, string? OperationId = null, string Phase = "Ready");
internal sealed record MoveEntry(string RelativePath, string SourceIdentity, string DestinationIdentity, string Hash);
internal sealed record MoveDirectory(string RelativePath, string SourceIdentity, string DestinationIdentity);
internal sealed record MoveJournal(string Id, string Source, string Destination, string Phase, IReadOnlyList<MoveEntry> Entries, IReadOnlyList<MoveDirectory> Directories);
internal sealed record WriteJournal(string Id, string Path, string Identity, string BeforeHash, string CandidateHash, string BeforePath, string CandidatePath);

internal static class DurableState
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static void Write<T>(string path, T value)
    {
        using var parents = DirectoryProtection.Acquire(Path.GetDirectoryName(path)!, true);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ConfigurationException("UnsafePath", "Internal state cannot be a reparse point.", path);
            WorkspaceNative.DurableMove(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static T Read<T>(string path)
    {
        using var file = ProtectedFile.Open(path);
        return JsonSerializer.Deserialize<T>(file.ReadBytes(16 * 1024 * 1024), Json) ?? throw new ConfigurationException("RecoveryRequired", "Internal operation state is empty.", path);
    }
}

internal sealed class LocatorStore(string stateDirectory, bool persistRegistry)
{
    private string Path => System.IO.Path.Combine(stateDirectory, "locator.json");
    public WorkspaceLocator? Read()
    {
        if (!persistRegistry) return File.Exists(Path) ? DurableState.Read<WorkspaceLocator>(Path) : null;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\CursorShake");
        string? value = key?.GetValue("Workspace") as string;
        return value is null ? null : JsonSerializer.Deserialize<WorkspaceLocator>(value, DurableState.Json) ?? throw new ConfigurationException("RecoveryRequired", "Workspace locator is invalid.");
    }
    public void Write(WorkspaceLocator locator, Action? afterPersistence = null)
    {
        if (!persistRegistry) { DurableState.Write(Path, locator); afterPersistence?.Invoke(); return; }
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\CursorShake", true);
        key.SetValue("Workspace", JsonSerializer.Serialize(locator, DurableState.Json), RegistryValueKind.String);
        afterPersistence?.Invoke();
        key.Flush();
    }
}
