using System.Runtime.InteropServices;
using System.Text.Json;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using SkiaSharp;

namespace CursorShake.NativeSmoke;

internal static class PreparedThemeSheet
{
    internal static void Create(string bundledRoot, string output)
    {
        using var theme = new ThemePreparer(bundledRoot).Prepare(new AppConfiguration(), Path.GetTempPath());
        const int columns = 5, cellWidth = 240, cellHeight = 230, size = 128;
        using var surface = SKSurface.Create(new SKImageInfo(columns * cellWidth, 4 * cellHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(235, 238, 242));
        using var font = new SKFont(SKTypeface.FromFamilyName("Consolas"), 16);
        using var smallFont = new SKFont(SKTypeface.FromFamilyName("Consolas"), 12);
        using var text = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var fill = new SKPaint();
        using var hot = new SKPaint { Color = SKColors.Red, StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        var metadata = new List<object>();
        int index = 0;
        foreach (var role in CursorRoles.All)
        {
            var frame = theme.Roles[role].Frames[0];
            var pixels = frame.Rasterize(size, size);
            float left = (index % columns) * cellWidth, top = (index / columns) * cellHeight;
            float x = left + 48, y = top + 40;
            canvas.DrawText(role.ToString(), left + 12, top + 23, SKTextAlign.Left, font, text);
            for (int row = 0; row < 8; row++) for (int col = 0; col < 8; col++)
            {
                fill.Color = (row + col) % 2 == 0 ? SKColors.White : new SKColor(220, 223, 228);
                canvas.DrawRect(x + col * 16, y + row * 16, 16, 16, fill);
            }
            using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            Marshal.Copy(pixels.Bgra, 0, bitmap.GetPixels(), pixels.Bgra.Length);
            using var rendered = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(rendered, x, y, new SKSamplingOptions(SKFilterMode.Nearest));
            float hx = x + pixels.HotspotX + 0.5f, hy = y + pixels.HotspotY + 0.5f;
            canvas.DrawCircle(hx, hy, 4, hot);
            canvas.DrawLine(hx - 7, hy, hx + 7, hy, hot);
            canvas.DrawLine(hx, hy - 7, hx, hy + 7, hot);
            canvas.DrawText(FormattableString.Invariant($"hot source=({frame.HotspotX:0.##},{frame.HotspotY:0.##})"), left + 12, top + 192, SKTextAlign.Left, smallFont, text);
            canvas.DrawText($"hot 128px=({pixels.HotspotX},{pixels.HotspotY})", left + 12, top + 211, SKTextAlign.Left, smallFont, text);
            metadata.Add(new { Role = role.ToString(), frame.Width, frame.Height, frame.HotspotX, frame.HotspotY, RasterHotspotX = pixels.HotspotX, RasterHotspotY = pixels.HotspotY });
            index++;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using (var file = File.Create(output)) png.SaveTo(file);
        File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }
}
