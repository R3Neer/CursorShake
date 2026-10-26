using CursorShake.Windows.Cursors.Formats;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class CursorRenderingTests
{
    [Fact]
    public void HotspotScalesWithTheFullCanvasAtLargeAndSmallSizes()
    {
        using var asset = LoadVector(TestCursorFiles.ArrowSvg);
        var frame = asset.Frames[0];
        foreach (var (size, x, y) in new[] { (32, 2, 1), (64, 4, 2), (256, 16, 8), (2048, 128, 64) })
        {
            var pixels = frame.Rasterize(size, size);
            Assert.Equal(size, pixels.Width);
            Assert.Equal(size, pixels.Height);
            Assert.Equal(x, pixels.HotspotX);
            Assert.Equal(y, pixels.HotspotY);
            Assert.Equal(size * size * 4, pixels.Bgra.Length);
        }
    }

    [Fact]
    public void VectorEdgesAreRenderedAtFinalResolutionAfterSmallRendering()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\"><rect x=\"10.25\" y=\"10\" width=\"20\" height=\"20\" fill=\"black\"/></svg>";
        using var asset = LoadVector(svg);
        asset.Frames[0].Rasterize(64, 64);
        var large = asset.Frames[0].Rasterize(512, 512);
        // The x=10.25 vector edge lands exactly at pixel82 after scaling by8.
        Assert.Equal(0, AlphaAt(large, 81, 120));
        Assert.Equal(255, AlphaAt(large, 82, 120));
        Assert.Equal(255, AlphaAt(large, 100, 120));
    }

    [Fact]
    public void SvgGaussianBlurIsPreservedOutsideTheUnfilteredShape()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\"><defs><filter id=\"blur\" x=\"-100%\" y=\"-100%\" width=\"300%\" height=\"300%\"><feGaussianBlur stdDeviation=\"2\"/></filter></defs><rect x=\"16\" y=\"16\" width=\"16\" height=\"16\" fill=\"black\" filter=\"url(#blur)\"/></svg>";
        using var asset = LoadVector(svg);
        var pixels = asset.Frames[0].Rasterize(64, 64);
        Assert.InRange(AlphaAt(pixels, 14, 24), 1, 254);
        Assert.InRange(AlphaAt(pixels, 24, 24), 200, 255);
    }

    [Fact]
    public void NativeRasterAlphaAndPremultipliedColorSurviveDecodeAndIdentityRendering()
    {
        using var asset = new CursorAssetLoader().Load(TestCursorFiles.Cur(argb: 0x80800000), ".cur");
        var pixels = asset.Frames[0].Rasterize(32, 32);
        Assert.Equal(0, pixels.Bgra[0]);
        Assert.Equal(0, pixels.Bgra[1]);
        Assert.Equal(128, pixels.Bgra[2]);
        Assert.Equal(128, pixels.Bgra[3]);
        Assert.Equal(4, pixels.HotspotX);
        Assert.Equal(2, pixels.HotspotY);
    }

    [Fact]
    public void RasterizationRejectsOversizedSurfacesBeforeAllocation()
    {
        using var asset = LoadVector(TestCursorFiles.ArrowSvg);
        var error = Assert.Throws<CursorAssetException>(() => asset.Frames[0].Rasterize(2049, 64));
        Assert.Equal(AssetErrorCode.LimitExceeded, error.Code);
    }

    [Theory]
    [InlineData("url(#blur)")]
    [InlineData("url( #blur )")]
    [InlineData("url(\"#blur\")")]
    [InlineData("url('#blur')")]
    public void InternalCssFilterReferencesRemainAllowedWithWhitespaceOrQuotes(string reference)
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\"><defs><filter id=\"blur\" x=\"-100%\" y=\"-100%\" width=\"300%\" height=\"300%\"><feGaussianBlur stdDeviation=\"2\"/></filter></defs><style>.blurred { filter: " + reference + "; }</style><rect class=\"blurred\" x=\"16\" y=\"16\" width=\"16\" height=\"16\" fill=\"black\"/></svg>";
        using var asset = LoadVector(svg);
        var pixels = asset.Frames[0].Rasterize(64, 64);
        Assert.InRange(AlphaAt(pixels, 14, 24), 1, 254);
    }

    [Fact]
    public void PngCompressedCurRetainsAlphaAndHotspot()
    {
        using var bitmap = new SkiaSharp.SKBitmap(32, 32);
        bitmap.Erase(new SkiaSharp.SKColor(255, 0, 0, 128));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var asset = new CursorAssetLoader().Load(TestCursorFiles.PngCur(png.ToArray(), 32, 32, 4, 2), ".cur");
        var pixels = asset.Frames[0].Rasterize(32, 32);
        Assert.Equal(128, pixels.Bgra[3]);
        Assert.Equal(4, pixels.HotspotX);
        Assert.Equal(2, pixels.HotspotY);
    }

    [Fact]
    public void MonochromeAndXorSemanticsSurviveScaling()
    {
        using var asset = new CursorAssetLoader().Load(TestCursorFiles.MonochromeCur(), ".cur");
        var pixels = asset.Frames[0].Rasterize(64, 64);
        // Inverse, transparent, white, black source pixels expand to pairs, retaining their masks.
        Assert.Equal(0xF0, pixels.AndMask[0]);
        Assert.Equal(255, pixels.Bgra[0]);
        Assert.Equal(0, pixels.Bgra[2 * 4]);
        Assert.Equal(255, pixels.Bgra[4 * 4]);
        Assert.Equal(0, pixels.Bgra[6 * 4]);
        Assert.All(Enumerable.Range(0, 64 * 64), i => Assert.Equal(0, pixels.Bgra[i * 4 + 3]));
    }

    private static CursorAsset LoadVector(string svg) => new CursorAssetLoader().Load(TestCursorFiles.VectorArchive("version=1\nimage=\"p.svg\"\nhotspot=[4,2]", ("p.svg", svg)), ".vcur");
    private static byte AlphaAt(CursorShake.Windows.Cursors.Native.CursorPixels pixels, int x, int y) => pixels.Bgra[(y * pixels.Width + x) * 4 + 3];
}
