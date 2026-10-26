using CursorShake.Windows.Recovery;
using System.Text.Json;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using Microsoft.Win32;

namespace CursorShake.NativeSmoke;

internal static class RegisteredSchemeProbe
{
    internal static int Run(string output)
    {
        var results = new List<object>(); int matched = 0, unknown = 0; string? failure = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            foreach (var role in CursorRoles.All)
            {
                var current = CursorNative.Capture(CursorNative.LoadCursor(0, (nint)(uint)role));
                string fingerprint = CursorFingerprint.Of(current);
                string? value = key?.GetValue(role.RegistryName()) as string;
                if (string.IsNullOrWhiteSpace(value))
                {
                    unknown++;
                    results.Add(new { Role = role.ToString(), RegisteredSource = value, CurrentFingerprint = fingerprint, Verified = false, Reason = "No explicit registered file; inherited system default cannot be independently verified from this registry entry." });
                    continue;
                }
                string path = Environment.ExpandEnvironmentVariables(value);
                if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors", path);
                using var source = CursorNative.LoadFile(path, current.Width, current.Height);
                string sourceFingerprint = CursorFingerprint.Of(CursorNative.Capture(source.DangerousGetHandle()));
                bool equal = sourceFingerprint == fingerprint;
                if (equal) matched++;
                results.Add(new { Role = role.ToString(), RegisteredSource = path, CurrentFingerprint = fingerprint, SourceFingerprint = sourceFingerprint, Verified = equal });
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        bool all = failure is null && matched == CursorRoles.All.Count;
        File.WriteAllText(output, JsonSerializer.Serialize(new { ProbeCompleted = failure is null, SystemCursorMutation = false, Matched = matched, Unknown = unknown, All17Verified = all, ResetNativePermittedByPreflight = all, Failure = failure, Cases = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
}
