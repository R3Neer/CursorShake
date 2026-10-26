namespace CursorShake.Windows.Configuration;

/// <summary>Reads a resource while every ancestor and the final file remain protected against replacement.</summary>
public static class WorkspaceResources
{
    public static byte[] ReadContained(string themeRoot, string relative, int maximumBytes)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(part => part is "" or "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ConfigurationException("UnsafePath", "Resource must be a relative path inside its theme.", relative);
        string path = WorkspacePaths.Child(WorkspacePaths.Canonical(themeRoot), relative);
        using var chain = DirectoryProtection.Acquire(Path.GetDirectoryName(path)!, denyDirectoryWrites: true);
        using var file = ProtectedFile.Open(path);
        return file.ReadBytes(maximumBytes);
    }
    internal static byte[] ReadSystemReference(string path, int maximumBytes)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        string known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors");
        string full = Path.GetFullPath(path);
        if (!WorkspacePaths.Same(Path.GetDirectoryName(full)!, known)) throw new ConfigurationException("UnsafePath", "System reference must be a direct file in the known Windows cursor directory.", full);
        using var chain = DirectoryProtection.Acquire(known, denyDirectoryWrites: true);
        // Windows servicing legitimately hard-links these readonly reference files to WinSxS.
        using var file = ProtectedFile.Open(full, allowHardLinks: true);
        return file.ReadBytes(maximumBytes);
    }
}
