using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CursorShake.Core.Shake;
using CursorShake.Windows.Cursors;
using CursorShake.App;

namespace CursorShake.NativeSmoke;

internal static class RuntimeProof
{
    internal static int Run(string output)
    {
        var files = new[] { typeof(ShakeDetector).Assembly, typeof(WindowsCursorEngine).Assembly, typeof(AppHost).Assembly }.Select(assembly => new { Assembly = assembly.GetName().Name, Path = assembly.Location, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { RuntimeVersion = Environment.Version.ToString(), RuntimeDescription = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Process = Environment.ProcessPath, Assemblies = files, SystemCursorMutation = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        return 0;
    }
}
