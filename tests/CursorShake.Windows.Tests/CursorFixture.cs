using System.IO.Compression;
using System.Text;

namespace CursorShake.Windows.Tests;

internal sealed class CursorFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "CursorShake.Tests", Guid.NewGuid().ToString("N"));
    public CursorFixture() => Directory.CreateDirectory(DirectoryPath);
    public string Write(string name, byte[] data)
    {
        var path = Path.Combine(DirectoryPath, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }
    public string WriteText(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));
    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
    }
}

internal static class TestCursorFiles
{
    public const string ArrowSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\"><path fill=\"white\" stroke=\"black\" d=\"M4 2 L4 50 L16 36 L30 58 L38 53 L24 32 L44 30 Z\"/></svg>";
    public static byte[] VectorArchive(string manifest, params (string Name, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("cursor.toml");
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(manifest);
            foreach (var (name, content) in files)
            {
                var file = zip.CreateEntry(name);
                using var writer = new StreamWriter(file.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return output.ToArray();
    }

    public static byte[] Cur(int width = 32, int height = 32, int hotspotX = 4, int hotspotY = 2, uint argb = 0xFFFFFFFF)
    {
        if (width is < 1 or > 256 || height is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(width));
        int maskStride = ((width + 31) / 32) * 4;
        int imageSize = 40 + width * height * 4 + maskStride * height;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
        writer.Write((byte)(width == 256 ? 0 : width)); writer.Write((byte)(height == 256 ? 0 : height));
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)hotspotX); writer.Write((ushort)hotspotY);
        writer.Write(imageSize); writer.Write(22);
        writer.Write(40); writer.Write(width); writer.Write(height * 2); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(0); writer.Write(width * height * 4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (int i = 0; i < width * height; i++) writer.Write(argb);
        writer.Write(new byte[maskStride * height]);
        return output.ToArray();
    }

    public static byte[] PngCur(byte[] png, int width, int height, int hotspotX, int hotspotY)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
        writer.Write((byte)(width == 256 ? 0 : width)); writer.Write((byte)(height == 256 ? 0 : height));
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)hotspotX); writer.Write((ushort)hotspotY);
        writer.Write(png.Length); writer.Write(22); writer.Write(png);
        return output.ToArray();
    }

    public static byte[] MonochromeCur()
    {
        const int width = 32, height = 32, stride = 4;
        int imageSize = 40 + 8 + stride * height * 2;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
        writer.Write((byte)width); writer.Write((byte)height); writer.Write((byte)2); writer.Write((byte)0);
        writer.Write((ushort)4); writer.Write((ushort)2); writer.Write(imageSize); writer.Write(22);
        writer.Write(40); writer.Write(width); writer.Write(height * 2); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write(0); writer.Write(stride * height); writer.Write(0); writer.Write(0); writer.Write(2); writer.Write(0);
        writer.Write(0u); writer.Write(0x00FFFFFFu);
        for (int y = 0; y < height; y++) { writer.Write((byte)0xA0); writer.Write(new byte[stride - 1]); }
        for (int y = 0; y < height; y++) { writer.Write((byte)0xC0); writer.Write(new byte[stride - 1]); }
        return output.ToArray();
    }

    public static byte[] Ani(byte[][] frames, uint[] rates, uint[] sequence)
    {
        if (rates.Length != sequence.Length) throw new ArgumentException("Each sequence step needs a rate.");
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("ACON"));
            using var header = new MemoryStream();
            using (var h = new BinaryWriter(header, Encoding.ASCII, true))
            {
                h.Write(36u); h.Write((uint)frames.Length); h.Write((uint)sequence.Length);
                h.Write(0u); h.Write(0u); h.Write(0u); h.Write(0u); h.Write(6u); h.Write(3u);
            }
            Chunk(writer, "anih", header.ToArray());
            Chunk(writer, "rate", rates.SelectMany(BitConverter.GetBytes).ToArray());
            Chunk(writer, "seq ", sequence.SelectMany(BitConverter.GetBytes).ToArray());
            using var imageList = new MemoryStream();
            using (var list = new BinaryWriter(imageList, Encoding.ASCII, true))
            {
                list.Write(Encoding.ASCII.GetBytes("fram"));
                foreach (var frame in frames) Chunk(list, "icon", frame);
            }
            Chunk(writer, "LIST", imageList.ToArray());
        }
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write((uint)body.Length); writer.Write(body.ToArray());
        }
        return output.ToArray();
    }

    private static void Chunk(BinaryWriter writer, string name, byte[] data)
    {
        writer.Write(Encoding.ASCII.GetBytes(name)); writer.Write((uint)data.Length); writer.Write(data);
        if ((data.Length & 1) != 0) writer.Write((byte)0);
    }
}
