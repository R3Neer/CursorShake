using System.Security.Cryptography;
using System.Text.Json;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;

namespace CursorShake.NativeSmoke;

internal static class NativeCapture
{
    internal static int Run(string stateDirectory, string reportPath)
    {
        var cases = new List<object>();
        long estimatedBytes = 0;
        string? failure = null;
        try
        {
            using var session = new NativeCursorSession(stateDirectory, "unused-for-capture-only.exe", startGuard: false);
            estimatedBytes = session.BaselineEstimatedBytes;
            if (session.BaselinePixels.Count != 17 || session.SharedHandles.Count != 17) throw new InvalidOperationException("Incomplete baseline role map.");
            foreach (var role in CursorRoles.All)
            {
                var pixels = session.BaselinePixels[role];
                if (session.SharedHandles[role] == 0 || pixels.Width <= 0 || pixels.Height <= 0) throw new InvalidOperationException("Invalid baseline metadata.");
                cases.Add(new { Role = role.ToString(), pixels.Width, pixels.Height, pixels.HotspotX, pixels.HotspotY, PixelHash = Convert.ToHexString(SHA256.HashData(pixels.Bgra)), MaskHash = Convert.ToHexString(SHA256.HashData(pixels.AndMask)) });
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Passed = failure is null, MutationAttempted = false, GuardStarted = false, BaselineEstimatedBytes = estimatedBytes, Failure = failure, Cases = cases }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(reportPath);
        if (failure is not null) { Console.Error.WriteLine(failure); return 1; }
        return 0;
    }
}
