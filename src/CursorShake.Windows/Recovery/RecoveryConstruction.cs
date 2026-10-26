using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CursorShake.Windows.Configuration;
namespace CursorShake.Windows.Recovery;

/// <summary>Tracks only identities created during this constructor, including failures before session.json exists.</summary>
internal sealed class RecoveryConstruction : IDisposable
{
    private readonly string _directory;
    private readonly string _identity;
    private readonly List<(string Path, string Identity, string Hash)> _files = [];
    private DirectoryProtection? _protection;
    private bool _complete;
    public RecoveryConstruction(string directory)
    {
        _directory = directory;
        using var parent = DirectoryProtection.Acquire(Path.GetDirectoryName(directory)!, true);
        if (!CreateDirectoryW(directory, IntPtr.Zero)) throw new IOException("Cannot create a new recovery session directory: " + directory, new Win32Exception(Marshal.GetLastWin32Error()));
        _protection = DirectoryProtection.Acquire(directory);
        _identity = WorkspaceNative.DirectoryIdentity(directory);
    }
    public void WriteBytes(string path, byte[] bytes)
    {
        using var file = ProtectedFile.Open(path, FileAccess.ReadWrite, FileMode.CreateNew, true);
        var entry = (path, file.Identity, Convert.ToHexString(SHA256.HashData(bytes)));
        _files.Add(entry);
        try { file.Stream.Write(bytes); file.Stream.Flush(true); if (file.Hash() != entry.Item3) throw new IOException("Recovery file verification failed: " + path); }
        catch
        {
            try { file.DeleteSameIdentity(); _files.Remove(entry); } catch { }
            throw;
        }
    }
    public void TrackExisting(string path)
    {
        if (_files.Any(f => WorkspacePaths.Same(f.Path, path))) return;
        using var file = ProtectedFile.Open(path); _files.Add((path, file.Identity, file.Hash()));
    }
    public void DeleteTracked(string path)
    {
        int index = _files.FindIndex(file => WorkspacePaths.Same(file.Path, path));
        if (index < 0) return;
        var expected = _files[index];
        using var file = ProtectedFile.Open(path, deleteAccess: true);
        if (file.Identity != expected.Identity || file.Hash() != expected.Hash) throw new IOException("Recovery file changed; preserving it: " + path);
        file.DeleteSameIdentity(); _files.RemoveAt(index);
    }
    public void ReleaseProtection() { _protection?.Dispose(); _protection = null; }
    public void Complete() { _complete = true; ReleaseProtection(); }
    public bool Cleanup()
    {
        ReleaseProtection(); bool complete = true;
        foreach (var entry in _files.ToArray())
        {
            try { DeleteTracked(entry.Path); }
            catch (FileNotFoundException) { _files.Remove(entry); }
            catch { complete = false; }
        }
        try { WorkspaceNative.RemoveEmptyDirectory(_directory, _identity); }
        catch (FileNotFoundException) { }
        catch { complete = false; }
        return complete;
    }
    public void Dispose() { if (!_complete) Cleanup(); else ReleaseProtection(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateDirectoryW(string path, IntPtr security);
}
