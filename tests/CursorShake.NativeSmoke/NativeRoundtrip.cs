using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Formats;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Tests;

namespace CursorShake.NativeSmoke;

internal static class NativeRoundtrip
{
    private sealed record Result(string Theme, string Role, int Width, int Height, int ExpectedHotX, int ExpectedHotY, int ObservedHotX, int ObservedHotY, int DifferentBytes, string ExpectedHash, string ObservedHash);

    internal static int Run(string bundledThemes, string output)
    {
        var results = new List<Result>();
        string? failure = null;
        try
        {
            foreach (string id in new[] { "windows", "bibata-ice", "nordzy-white" })
            {
                using var theme = new ThemePreparer(bundledThemes).Prepare(new AppConfiguration { Theme = id }, Path.GetTempPath());
                foreach (var pair in theme.Roles)
                    foreach (int size in new[] { 32, 96 })
                        Verify(id, pair.Key.ToString(), pair.Value.Frames[0], size, size);
                foreach (int size in new[] { 512, 2048 })
                    Verify(id, "WaitLarge", theme.Roles[CursorRole.Wait].Frames[0], size, size);
            }
            var loader = new CursorAssetLoader();
            using var odd = loader.Load(TestCursorFiles.Cur(width: 15, height: 19, hotspotX: 4, hotspotY: 2), ".cur");
            Verify("synthetic", "OddMaskStride", odd.Frames[0], 15, 19);
            using var legacy = loader.Load(TestCursorFiles.MonochromeCur(), ".cur");
            Verify("synthetic", "Monochrome", legacy.Frames[0], 64, 64);
        }
        catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { SystemCursorMutation = false, CaseCount = results.Count, Passed = failure is null, Failure = failure, Cases = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        if (failure is not null) { Console.Error.WriteLine(failure); return 1; }
        return 0;

        void Verify(string theme, string role, CursorFrame frame, int width, int height)
        {
            var expected = frame.Rasterize(width, height);
            using var handle = frame.CreateCursor(width, height);
            var actual = Read(handle);
            int differences = actual.Pixels.Zip(expected.Bgra, (a, b) => a == b ? 0 : 1).Sum();
            results.Add(new(theme, role, width, height, expected.HotspotX, expected.HotspotY, actual.HotX, actual.HotY, differences, Convert.ToHexString(SHA256.HashData(expected.Bgra)), Convert.ToHexString(SHA256.HashData(actual.Pixels))));
            if (actual.Width != width || actual.Height != height || actual.HotX != expected.HotspotX || actual.HotY != expected.HotspotY || differences != 0)
                throw new InvalidOperationException($"Native roundtrip differs for {theme}/{role} at {width}x{height}: different bytes={differences}, hotspot=({actual.HotX},{actual.HotY}).");
        }
    }

    private static (int Width, int Height, int HotX, int HotY, byte[] Pixels) Read(SafeCursorHandle cursor)
    {
        if (!GetIconInfo(cursor.DangerousGetHandle(), out var icon)) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint dc = GetDC(0);
        try
        {
            if (icon.Color == 0) throw new InvalidDataException("Expected the engine's color bitmap cursor.");
            if (GetObject(icon.Color, Marshal.SizeOf<BitmapObject>(), out var bitmap) == 0) throw new Win32Exception();
            var info = new BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, BitCount = 32 };
            byte[] pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            if (GetDIBits(dc, icon.Color, 0, (uint)bitmap.Height, pixels, ref info, 0) != bitmap.Height) throw new Win32Exception();
            return (bitmap.Width, bitmap.Height, (int)icon.HotX, (int)icon.HotY, pixels);
        }
        finally
        {
            if (dc != 0) ReleaseDC(0, dc);
            if (icon.Color != 0) DeleteObject(icon.Color);
            if (icon.Mask != 0) DeleteObject(icon.Mask);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon; public uint HotX, HotY; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapObject { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XP, YP; public uint Used, Important; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetIconInfo(nint cursor, out IconInfo info);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(nint obj, int size, out BitmapObject bitmap);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint count, byte[] pixels, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint bitmap);
}
