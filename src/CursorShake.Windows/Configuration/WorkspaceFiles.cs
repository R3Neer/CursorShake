using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace CursorShake.Windows.Configuration;

internal static class WorkspacePaths
{
    public static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static bool Same(string a, string b) => StringComparer.OrdinalIgnoreCase.Equals(Canonical(a), Canonical(b));
    public static bool Inside(string path, string parent) => Canonical(path).StartsWith(Canonical(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string Child(string root, string relative)
    {
        var full = Path.GetFullPath(relative, root);
        if (!Inside(full, root)) throw new ConfigurationException("UnsafePath", "Path escapes the managed root.", full);
        return full;
    }
}

internal sealed class DirectoryProtection : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    public static DirectoryProtection Acquire(string directory, bool create = false, bool denyDirectoryWrites = false)
    {
        var result = new DirectoryProtection();
        try
        {
            string absolute = Path.GetFullPath(directory);
            string current = Path.GetPathRoot(absolute) ?? throw new IOException("Directory has no volume root.");
            result.Add(current, denyDirectoryWrites);
            foreach (var component in absolute[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                if (create) Directory.CreateDirectory(current);
                result.Add(current, denyDirectoryWrites);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private void Add(string directory, bool denyDirectoryWrites)
    {
        var handle = WorkspaceNative.Open(directory, 0x80, denyDirectoryWrites ? FileShare.Read : FileShare.Read | FileShare.Write, FileMode.Open, 0x02000000 | 0x00200000);
        try
        {
            var info = WorkspaceNative.Info(handle, directory);
            if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.Attributes & (uint)FileAttributes.Directory) == 0) throw new ConfigurationException("UnsafePath", "Directory is a link/reparse point or not a directory.", directory);
            _handles.Add(handle);
        }
        catch { handle.Dispose(); throw; }
    }
    public void Dispose() { for (int i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); _handles.Clear(); }
}

internal sealed class ProtectedFile : IDisposable
{
    private readonly DirectoryProtection _parents;
    public FileStream Stream { get; }
    public string Path { get; }
    public string Identity { get; }
    public static ProtectedFile Open(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.Open, bool deleteAccess = false, bool allowHardLinks = false)
    {
        if (allowHardLinks && (access != FileAccess.Read || mode != FileMode.Open || deleteAccess)) throw new ArgumentException("Hard-linked files may only be opened for existing read-only system references.", nameof(allowHardLinks));
        var parents = DirectoryProtection.Acquire(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        SafeFileHandle? handle = null;
        try
        {
            uint desired = access switch { FileAccess.Read => 0x80000000, FileAccess.Write => 0x40000000, _ => 0xC0000000 };
            if (deleteAccess) desired |= 0x10000;
            handle = WorkspaceNative.Open(path, desired, FileShare.Read, mode, 0x00200000);
            var info = WorkspaceNative.Info(handle, path);
            if ((!allowHardLinks && info.Links > 1) || (info.Attributes & ((uint)FileAttributes.ReparsePoint | (uint)FileAttributes.Directory)) != 0) throw new ConfigurationException("UnsafePath", "A managed file cannot be a link, reparse point or directory.", path);
            return new ProtectedFile(path, parents, handle, access, WorkspaceNative.Identity(info));
        }
        catch { handle?.Dispose(); parents.Dispose(); throw; }
    }
    private ProtectedFile(string path, DirectoryProtection parents, SafeFileHandle handle, FileAccess access, string identity)
    { Path = path; _parents = parents; Identity = identity; Stream = new FileStream(handle, access, 65536, false); }
    public string Hash() { Stream.Position = 0; var hash = Convert.ToHexString(SHA256.HashData(Stream)); Stream.Position = 0; return hash; }
    public byte[] ReadBytes(int maximum = ConfigParser.MaximumTextBytes)
    {
        if (Stream.Length > maximum) throw new ConfigurationException("LimitExceeded", "Managed text exceeds its byte limit.", Path);
        Stream.Position = 0; var bytes = new byte[checked((int)Stream.Length)]; Stream.ReadExactly(bytes); Stream.Position = 0; return bytes;
    }
    public string ReadText() => Decode(ReadBytes());
    public static string Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        return reader.ReadToEnd();
    }
    public void DeleteSameIdentity() => WorkspaceNative.MarkDelete(Stream.SafeFileHandle, Path);
    public void Dispose() { Stream.Dispose(); _parents.Dispose(); }
}

internal static class WorkspaceNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct FileInfo
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.Bool)] public bool Delete; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref Disposition information, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool MoveFileExW(string existing, string destination, uint flags);
    public static SafeFileHandle Open(string path, uint access, FileShare share, FileMode mode, uint flags)
    {
        uint creation = mode switch { FileMode.Open => 3, FileMode.CreateNew => 1, FileMode.OpenOrCreate => 4, _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
        var handle = CreateFileW(path, access, share, IntPtr.Zero, creation, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastWin32Error(); handle.Dispose();
        if (error is 32 or 33) throw new ConfigurationException("Busy", "A file or directory is in use by an incompatible writer or operation.", path, inner: new Win32Exception(error));
        if (error is 2 or 3) throw new FileNotFoundException("Managed path does not exist.", path, new Win32Exception(error));
        throw new IOException("Cannot open managed path: " + path, new Win32Exception(error));
    }
    public static FileInfo Info(SafeFileHandle handle, string path)
    { if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Cannot read file identity: " + path, new Win32Exception(Marshal.GetLastWin32Error())); return info; }
    public static string Identity(FileInfo info) => $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
    public static void MarkDelete(SafeFileHandle handle, string path)
    {
        var info = new Disposition { Delete = true };
        if (!SetFileInformationByHandle(handle, 4, ref info, (uint)Marshal.SizeOf<Disposition>())) throw new IOException("Cannot retire verified identity: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
    }
    public static string DirectoryIdentity(string path)
    {
        using var protection = DirectoryProtection.Acquire(path);
        using var handle = Open(path, 0x80, FileShare.Read | FileShare.Write, FileMode.Open, 0x02000000 | 0x00200000);
        return Identity(Info(handle, path));
    }
    public static void RemoveEmptyDirectory(string path, string expectedIdentity)
    {
        using var parents = DirectoryProtection.Acquire(System.IO.Path.GetDirectoryName(path)!);
        using var handle = Open(path, 0x80 | 0x10000, FileShare.Read | FileShare.Write, FileMode.Open, 0x02000000 | 0x00200000);
        var info = Info(handle, path);
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || Identity(info) != expectedIdentity) throw new ConfigurationException("Conflict", "Directory identity changed; preserving it.", path);
        // FileDispositionInfo rejects nonempty directories; no stale recursive deletion.
        MarkDelete(handle, path);
    }
    public static void DurableMove(string temporary, string destination)
    { if (!MoveFileExW(temporary, destination, 0x1 | 0x8)) throw new IOException("Cannot commit internal state: " + destination, new Win32Exception(Marshal.GetLastWin32Error())); }
}
