using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using Microsoft.Win32;
namespace CursorShake.Windows.Recovery;

internal static class BaselineStore
{
    public static RecoveryManifest Capture(string sessionDirectory, RecoveryConstruction construction, Action<RecoveryCheckpoint>? checkpoint, out IReadOnlyDictionary<CursorRole, CursorPixels> captured, out IReadOnlyDictionary<CursorRole, nint> handles)
    {
        var pixels = new Dictionary<CursorRole, CursorPixels>(); var shared = new Dictionary<CursorRole, nint>(); var records = new List<BaselineCursor>();
        using var parent = Process.GetCurrentProcess();
        foreach (var role in CursorRoles.All)
        {
            nint handle = CursorNative.LoadCursor(0, (nint)(uint)role);
            if (handle == 0) throw new InvalidOperationException("Shared cursor is unavailable: " + role);
            var image = CursorNative.Capture(handle); string fingerprint = CursorFingerprint.Of(image);
            pixels.Add(role, image); shared.Add(role, handle);
            string pixelFile = ((uint)role) + ".pixels";
            WritePixels(Path.Combine(sessionDirectory, pixelFile), image, construction);
            var source = TryPreserveSource(sessionDirectory, role, image, fingerprint, construction);
            records.Add(new(role, (long)handle, pixelFile, fingerprint, source.File, source.Hash, source.Bytes));
            checkpoint?.Invoke(RecoveryCheckpoint.BaselineCursorSaved);
        }
        captured = new System.Collections.ObjectModel.ReadOnlyDictionary<CursorRole, CursorPixels>(pixels);
        handles = new System.Collections.ObjectModel.ReadOnlyDictionary<CursorRole, nint>(shared);
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Current user SID is unavailable.");
        var manifest = new RecoveryManifest(Path.GetFileName(sessionDirectory), parent.Id, parent.StartTime.ToUniversalTime().Ticks, parent.SessionId, sid, records);
        string manifestPath = Path.Combine(sessionDirectory, "session.json");
        try { DurableState.Write(manifestPath, manifest); }
        finally { if (File.Exists(manifestPath)) construction.TrackExisting(manifestPath); }
        checkpoint?.Invoke(RecoveryCheckpoint.BaselineManifestSaved);
        return manifest;
    }
    private static (string? File, string? Hash, long Bytes) TryPreserveSource(string directory, CursorRole role, CursorPixels baseline, string fingerprint, RecoveryConstruction construction)
    {
        var candidates = new List<string>();
        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
        {
            if (key?.GetValue(role.RegistryName()) is string value && !string.IsNullOrWhiteSpace(value))
            {
                string expanded = Environment.ExpandEnvironmentVariables(value);
                candidates.Add(Path.IsPathRooted(expanded) ? expanded : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors", expanded));
            }
        }
        if (role is CursorRole.Wait or CursorRole.Working)
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors", role == CursorRole.Wait ? "aero_busy.ani" : "aero_working.ani"));
        bool animatedSourceUnverified = false;
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string extension = Path.GetExtension(candidate).ToLowerInvariant();
            if (extension is not ".ani" and not ".cur" || !File.Exists(candidate)) continue;
            if (extension == ".ani") animatedSourceUnverified = true;
            string localName = ((uint)role) + extension, localPath = Path.Combine(directory, localName);
            try
            {
                byte[] bytes;
                using (var original = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (original.Length > 16 * 1024 * 1024 || original.Length < 1) continue;
                    bytes = new byte[checked((int)original.Length)]; original.ReadExactly(bytes);
                }
                construction.WriteBytes(localPath, bytes);
                using var sourceProtection = ProtectedFile.Open(localPath);
                using var reloaded = CursorNative.LoadFile(localPath, baseline.Width, baseline.Height);
                if (CursorFingerprint.Of(CursorNative.Capture(reloaded.DangerousGetHandle())) == fingerprint)
                    return (localName, Convert.ToHexString(SHA256.HashData(bytes)), bytes.LongLength);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidDataException) { }
            if (File.Exists(localPath)) construction.DeleteTracked(localPath);
        }
        if (animatedSourceUnverified) throw new InvalidOperationException("Cannot verify the original animation for " + role + "; no system cursor was changed.");
        return (null, null, 0);
    }
    public static RecoveryManifest ReadManifest(string directory)
    {
        using var protection = DirectoryProtection.Acquire(directory);
        var manifest = DurableState.Read<RecoveryManifest>(Path.Combine(directory, "session.json"));
        if (!Guid.TryParseExact(manifest.Nonce, "N", out _) || Path.GetFileName(directory) != manifest.Nonce || manifest.ParentPid <= 0 || manifest.ParentStartTicks <= 0 || manifest.Cursors.Count != CursorRoles.All.Count || manifest.Cursors.Select(c => c.Role).Distinct().Count() != manifest.Cursors.Count || manifest.Cursors.Any(c => !Enum.IsDefined(c.Role))) throw new InvalidDataException("Invalid recovery manifest identity.");
        foreach (var cursor in manifest.Cursors)
        {
            if (cursor.PixelFile != ((uint)cursor.Role) + ".pixels" || cursor.Fingerprint.Length != 64) throw new InvalidDataException("Invalid baseline entry.");
            if (cursor.SourceFile is not null && (cursor.SourceFile != ((uint)cursor.Role) + ".ani" && cursor.SourceFile != ((uint)cursor.Role) + ".cur")) throw new InvalidDataException("Invalid baseline source path.");
        }
        return manifest;
    }
    public static CursorPixels ReadPixels(string path)
    {
        using var file = ProtectedFile.Open(path); using var reader = new BinaryReader(file.Stream, System.Text.Encoding.UTF8, true);
        if (reader.ReadInt32() != 0x43535031) throw new InvalidDataException("Invalid baseline pixel header.");
        int width = reader.ReadInt32(), height = reader.ReadInt32(), x = reader.ReadInt32(), y = reader.ReadInt32();
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || x < 0 || x >= width || y < 0 || y >= height) throw new InvalidDataException("Invalid baseline geometry.");
        int pixelLength = checked(width * height * 4), maskLength = checked(((width + 31) / 32) * 4 * height);
        var pixels = reader.ReadBytes(pixelLength); var mask = reader.ReadBytes(maskLength);
        if (pixels.Length != pixelLength || mask.Length != maskLength || file.Stream.Position != file.Stream.Length) throw new InvalidDataException("Invalid baseline payload length.");
        return new(width, height, x, y, pixels, mask);
    }
    private static void WritePixels(string path, CursorPixels pixels, RecoveryConstruction construction)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write(0x43535031); writer.Write(pixels.Width); writer.Write(pixels.Height); writer.Write(pixels.HotspotX); writer.Write(pixels.HotspotY); writer.Write(pixels.Bgra); writer.Write(pixels.AndMask); writer.Flush();
        construction.WriteBytes(path, stream.ToArray());
    }
    public static bool WriteResult(string directory, RecoveryManifest expected, RestoreResult result)
    {
        using var protection = ProtectExistingSession(directory);
        if (protection is null) return false;
        RecoveryManifest current;
        try { current = ReadManifest(directory); } catch (FileNotFoundException) { return false; }
        if (current.Nonce != expected.Nonce || current.ParentPid != expected.ParentPid || current.ParentStartTicks != expected.ParentStartTicks || current.OwnerSid != expected.OwnerSid || current.SessionId != expected.SessionId) return false;
        // The directory is pinned before DurableState runs: it cannot recreate a retired session.
        DurableState.Write(Path.Combine(directory, "result.json"), result);
        return true;
    }
    internal static DirectoryProtection? ProtectExistingSession(string directory)
    {
        try { return DirectoryProtection.Acquire(directory); }
        catch (FileNotFoundException) { return null; }
    }
    public static bool ExistsWithIdentity(string directory, RecoveryManifest expected)
    {
        using var protection = ProtectExistingSession(directory);
        if (protection is null) return false;
        var current = ReadManifest(directory);
        if (current.Nonce != expected.Nonce || current.ParentPid != expected.ParentPid || current.ParentStartTicks != expected.ParentStartTicks || current.OwnerSid != expected.OwnerSid || current.SessionId != expected.SessionId) throw new InvalidDataException("Recovery session identity changed.");
        return true;
    }
    internal static void ValidateCleanupEntries(string directory, RecoveryManifest manifest)
    {
        var allowed = manifest.Cursors.Select(c => c.PixelFile).Concat(manifest.Cursors.Where(c => c.SourceFile is not null).Select(c => c.SourceFile!))
            .Concat(new[] { "session.json", "result.json" }).ToHashSet(StringComparer.Ordinal);
        if (Directory.EnumerateFileSystemEntries(directory).Any(path => !allowed.Contains(Path.GetFileName(path)) || Directory.Exists(path)))
            throw new InvalidDataException("Unknown recovery entries were preserved; no receipt metadata was removed: " + directory);
    }
    public static void Cleanup(string directory, RecoveryManifest manifest, string? expectedDirectoryIdentity = null, Action? beforeDirectoryRemoval = null)
    {
        var names = manifest.Cursors.Select(c => c.PixelFile).Concat(manifest.Cursors.Where(c => c.SourceFile is not null).Select(c => c.SourceFile!)).Append("result.json").ToList();
        string identity; byte[] manifestBytes;
        using (var protection = ProtectExistingSession(directory))
        {
            if (protection is null) return;
            identity = WorkspaceNative.DirectoryIdentity(directory);
            if (expectedDirectoryIdentity is not null && identity != expectedDirectoryIdentity) throw new InvalidDataException("Recovery directory identity changed.");
            if (!ExistsWithIdentity(directory, manifest)) return;
            ValidateCleanupEntries(directory, manifest);
            using var metadata = ProtectedFile.Open(Path.Combine(directory, "session.json"), deleteAccess: true);
            manifestBytes = metadata.ReadBytes();
            foreach (var name in names)
            {
                string path = WorkspacePaths.Child(directory, name);
                try { using var file = ProtectedFile.Open(path, deleteAccess: true); file.DeleteSameIdentity(); } catch (FileNotFoundException) { }
            }
            // Keep the readable receipt until every other known entry is gone.
            if (Directory.EnumerateFileSystemEntries(directory).Any(path => Path.GetFileName(path) != "session.json"))
                throw new IOException("An entry appeared during recovery cleanup; the manifest was preserved.");
            metadata.DeleteSameIdentity();
        }
        try { beforeDirectoryRemoval?.Invoke(); WorkspaceNative.RemoveEmptyDirectory(directory, identity); }
        catch (FileNotFoundException) { }
        catch (Exception cleanupError)
        {
            // A late creator can make the empty-directory retirement fail. Restore only the original
            // verified bytes into the same directory identity with CreateNew; never replace a new file.
            using var remaining = ProtectExistingSession(directory);
            if (remaining is null) return;
            if (WorkspaceNative.DirectoryIdentity(directory) != identity) throw new IOException("Recovery directory changed during retirement; its replacement was preserved.", cleanupError);
            try
            {
                using var metadata = ProtectedFile.Open(Path.Combine(directory, "session.json"), FileAccess.ReadWrite, FileMode.CreateNew);
                metadata.Stream.Write(manifestBytes); metadata.Stream.Flush(true);
            }
            catch (IOException e) when (e.InnerException is System.ComponentModel.Win32Exception native && native.NativeErrorCode is 80 or 183) { }
            throw new IOException("Recovery cleanup could not finish; the directory and receipt were preserved.", cleanupError);
        }
    }
}
