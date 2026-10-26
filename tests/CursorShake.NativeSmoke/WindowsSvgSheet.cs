using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SkiaSharp;
using Svg.Skia;

namespace CursorShake.NativeSmoke;

internal static class WindowsSvgSheet
{
    internal static void Create(string output)
    {
        string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors");
        string[] names = ["arrow", "ibeam", "cross", "up", "pen", "nwse", "nesw", "ew", "ns", "move", "unavail", "link", "helpsel", "pin", "person"];
        const int columns = 5, cellWidth = 240, cellHeight = 220, iconSize = 96;
        using var surface = SKSurface.Create(new SKImageInfo(columns * cellWidth, 3 * cellHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(235, 238, 242));
        using var font = new SKFont(SKTypeface.FromFamilyName("Consolas"), 16);
        using var smallFont = new SKFont(SKTypeface.FromFamilyName("Consolas"), 11);
        using var text = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var grid = new SKPaint { Color = new SKColor(160, 165, 172), StrokeWidth = 1, IsAntialias = false };
        using var fill = new SKPaint();
        var metadata = new List<object>();
        for (int index = 0; index < names.Length; index++)
        {
            string name = names[index], path = Path.Combine(source, name + ".svg");
            var document = XDocument.Load(path);
            var root = document.Root ?? throw new InvalidDataException("No SVG root.");
            var view = ((string?)root.Attribute("viewBox") ?? "").Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            if (view.Length != 4) throw new InvalidDataException("SVG needs viewBox.");
            root.SetAttributeValue("width", view[2].ToString("R", CultureInfo.InvariantCulture));
            root.SetAttributeValue("height", view[3].ToString("R", CultureInfo.InvariantCulture));
            using var svg = new SKSvg();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToString()));
            if (svg.Load(stream) is null) throw new InvalidDataException("SVG render failed: " + name);
            float left = (index % columns) * cellWidth, top = (index / columns) * cellHeight;
            canvas.DrawText(name + ".svg", left + 12, top + 23, SKTextAlign.Left, font, text);
            const float inset = 48;
            float x = left + inset, y = top + 47;
            for (int row = 0; row < 8; row++) for (int col = 0; col < 8; col++)
            {
                fill.Color = (row + col) % 2 == 0 ? SKColors.White : new SKColor(220, 223, 228);
                canvas.DrawRect(x + col * 12, y + row * 12, 12, 12, fill);
            }
            for (int tick = 0; tick <= 4; tick++)
            {
                float offset = tick * iconSize / 4f;
                canvas.DrawLine(x + offset, y, x + offset, y + iconSize, grid);
                canvas.DrawLine(x, y + offset, x + iconSize, y + offset, grid);
                canvas.DrawText((view[0] + tick * view[2] / 4).ToString("0.#", CultureInfo.InvariantCulture), x + offset - 3, y - 6, SKTextAlign.Left, smallFont, text);
                canvas.DrawText((view[1] + tick * view[3] / 4).ToString("0.#", CultureInfo.InvariantCulture), x - 24, y + offset + 4, SKTextAlign.Left, smallFont, text);
            }
            canvas.Save();
            canvas.Translate(x, y);
            canvas.Scale((float)(iconSize / view[2]), (float)(iconSize / view[3]));
            canvas.DrawPicture(svg.Picture!);
            canvas.Restore();
            canvas.DrawText("viewBox " + string.Join(" ", view.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture))), left + 12, top + 175, SKTextAlign.Left, smallFont, text);
            canvas.DrawText("96px; grid in source units", left + 12, top + 195, SKTextAlign.Left, smallFont, text);
            metadata.Add(new { Name = name, ViewBox = view, Source = path, SourceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        using (var file = File.Create(output)) png.SaveTo(file);
        File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }
}
