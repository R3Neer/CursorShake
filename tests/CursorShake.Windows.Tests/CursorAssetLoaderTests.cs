using CursorShake.Windows.Cursors.Formats;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class CursorAssetLoaderTests
{
    private readonly CursorAssetLoader _loader = new();
    [Fact]
    public void StaticVectorPreservesSourceCoordinatesAndHotspot()
    {
        using var asset = _loader.Load(TestCursorFiles.VectorArchive("version = 1\nimage = \"pointer.svg\"\nhotspot = [4.0, 2.0]\n", ("pointer.svg", TestCursorFiles.ArrowSvg)), ".vcur");
        var frame = Assert.Single(asset.Frames);
        Assert.True(frame.IsVector);
        Assert.Equal(64, frame.Width);
        Assert.Equal(64, frame.Height);
        Assert.Equal(4, frame.HotspotX);
        Assert.Equal(2, frame.HotspotY);
        Assert.False(asset.IsAnimated);
    }

    [Fact]
    public void NonzeroViewBoxOriginIsAppliedToHotspot()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"10 20 64 64\"><rect x=\"10\" y=\"20\" width=\"64\" height=\"64\"/></svg>";
        using var asset = _loader.Load(TestCursorFiles.VectorArchive("version=1\nimage=\"p.svg\"\nhotspot=[14,22]", ("p.svg", svg)), ".vcur");
        Assert.Equal(4, asset.Frames[0].HotspotX);
        Assert.Equal(2, asset.Frames[0].HotspotY);
    }

    [Fact]
    public void VaniUsesManifestOrderDurationsHotspotOverridesAndSharedSources()
    {
        const string manifest = "version=1\nhotspot=[32,32]\n[[frames]]\nimage=\"a.svg\"\nduration_ms=50\n[[frames]]\nimage=\"b.svg\"\nduration_ms=100\nhotspot=[31,32]\n[[frames]]\nimage=\"a.svg\"\nduration_ms=50\n";
        using var asset = _loader.Load(TestCursorFiles.VectorArchive(manifest, ("a.svg", TestCursorFiles.ArrowSvg), ("b.svg", TestCursorFiles.ArrowSvg)), ".vani");
        Assert.True(asset.IsAnimated);
        Assert.Equal(2, asset.Frames.Count);
        Assert.Equal(new[] { 0, 1, 0 }, asset.Steps.Select(s => s.FrameIndex));
        Assert.Equal(new[] { 50d, 100d, 50d }, asset.Steps.Select(s => s.Duration.TotalMilliseconds));
        Assert.Equal(200, asset.Duration.TotalMilliseconds);
        Assert.Equal(32, asset.Frames[0].HotspotX);
        Assert.Equal(31, asset.Frames[1].HotspotX);
        foreach (var (time, frame) in new[] { (0, 0), (49, 0), (50, 1), (149, 1), (150, 0), (199, 0), (200, 0), (250, 1) })
            Assert.Equal(frame, asset.FrameIndexAt(TimeSpan.FromMilliseconds(time)));
    }

    [Theory]
    [InlineData("version=2\nimage=\"p.svg\"\nhotspot=[4,2]", AssetErrorCode.Unsupported)]
    [InlineData("version=1\nimage=\"p.svg\"", AssetErrorCode.Invalid)]
    [InlineData("version=1\nimage=\"p.svg\"\nhotspot=[65,2]", AssetErrorCode.Invalid)]
    [InlineData("version=1\nimage=\"p.svg\"\nhotspot=[4,2]\nsurprise=true", AssetErrorCode.Invalid)]
    public void InvalidOrUnsupportedVectorMetadataIsRejected(string manifest, AssetErrorCode code)
    {
        var bytes = TestCursorFiles.VectorArchive(manifest, ("p.svg", TestCursorFiles.ArrowSvg));
        var error = Assert.Throws<CursorAssetException>(() => _loader.Load(bytes, ".vcur"));
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void VaniRejectsNonPositiveStepDuration(int duration)
    {
        var manifest = $"version=1\nhotspot=[4,2]\n[[frames]]\nimage=\"p.svg\"\nduration_ms={duration}";
        Assert.Throws<CursorAssetException>(() => _loader.Load(TestCursorFiles.VectorArchive(manifest, ("p.svg", TestCursorFiles.ArrowSvg)), ".vani"));
    }

    [Theory]
    [InlineData("../escape.svg")]
    [InlineData("/root.svg")]
    [InlineData("C:/drive.svg")]
    [InlineData("p.svg:stream")]
    public void UnsafeZipEntriesAreRejectedEvenWhenNotReferenced(string entryName)
    {
        var bytes = TestCursorFiles.VectorArchive("version=1\nimage=\"p.svg\"\nhotspot=[4,2]", ("p.svg", TestCursorFiles.ArrowSvg), (entryName, TestCursorFiles.ArrowSvg));
        Assert.Throws<CursorAssetException>(() => _loader.Load(bytes, ".vcur"));
    }

    [Fact]
    public void CaseCollidingZipEntriesAreRejected()
    {
        var bytes = TestCursorFiles.VectorArchive("version=1\nimage=\"p.svg\"\nhotspot=[4,2]", ("p.svg", TestCursorFiles.ArrowSvg), ("P.SVG", TestCursorFiles.ArrowSvg));
        Assert.Throws<CursorAssetException>(() => _loader.Load(bytes, ".vcur"));
    }

    [Theory]
    [InlineData("<image href=\"https://example.invalid/a.svg\"/>")]
    [InlineData("<script>throw 'must not execute';</script>")]
    [InlineData("<style>path { fill: url(https://example.invalid/a.svg); }</style>")]
    public void SvgCannotReferenceExternalResourcesOrExecuteScripts(string content)
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\">{content}</svg>";
        var bytes = TestCursorFiles.VectorArchive("version=1\nimage=\"p.svg\"\nhotspot=[4,2]", ("p.svg", svg));
        Assert.Throws<CursorAssetException>(() => _loader.Load(bytes, ".vcur"));
    }

    [Fact]
    public void SvgSourceLimitIsCheckedEvenForHighlyCompressibleEntries()
    {
        var huge = TestCursorFiles.ArrowSvg + new string(' ', 4 * 1024 * 1024);
        var bytes = TestCursorFiles.VectorArchive("version=1\nimage=\"p.svg\"\nhotspot=[4,2]", ("p.svg", huge));
        var error = Assert.Throws<CursorAssetException>(() => _loader.Load(bytes, ".vcur"));
        Assert.Equal(AssetErrorCode.LimitExceeded, error.Code);
    }

    [Fact]
    public void NativeCurPreservesRasterHotspotAndDimensions()
    {
        using var asset = _loader.Load(TestCursorFiles.Cur(), ".cur");
        var frame = Assert.Single(asset.Frames);
        Assert.False(frame.IsVector);
        Assert.Equal(32, frame.Width);
        Assert.Equal(32, frame.Height);
        Assert.Equal(4, frame.HotspotX);
        Assert.Equal(2, frame.HotspotY);
    }

    [Fact]
    public void AniPreservesSequenceAndFractionalJiffyDurations()
    {
        var data = TestCursorFiles.Ani([TestCursorFiles.Cur(argb: 0xFF00FF00), TestCursorFiles.Cur(argb: 0xFFFF0000)], [1, 2, 3], [1, 0, 1]);
        using var asset = _loader.Load(data, ".ani");
        Assert.Equal(new[] { 1, 0, 1 }, asset.Steps.Select(s => s.FrameIndex));
        Assert.Equal(1d / 60, asset.Steps[0].Duration.TotalSeconds, 6);
        Assert.Equal(2d / 60, asset.Steps[1].Duration.TotalSeconds, 6);
        Assert.Equal(3d / 60, asset.Steps[2].Duration.TotalSeconds, 6);
        Assert.Equal(0.1, asset.Duration.TotalSeconds, 6);
    }

    [Fact]
    public void TruncatedCurAndInvalidAniFrameReferencesFailWithoutPartialAssets()
    {
        Assert.Throws<CursorAssetException>(() => _loader.Load(TestCursorFiles.Cur()[..20], ".cur"));
        var badSequence = TestCursorFiles.Ani([TestCursorFiles.Cur()], [6], [2]);
        Assert.Throws<CursorAssetException>(() => _loader.Load(badSequence, ".ani"));
    }

    [Fact]
    public void PositiveDurationThatRoundsToZeroClockTicksIsRejected()
    {
        var text = "version=1\nhotspot=[4,2]\n[[frames]]\nimage=\"p.svg\"\nduration_ms=0.00000001";
        var error = Assert.Throws<CursorAssetException>(() => _loader.Load(TestCursorFiles.VectorArchive(text, ("p.svg", TestCursorFiles.ArrowSvg)), ".vani"));
        Assert.Equal(AssetErrorCode.Invalid, error.Code);
    }

    [Fact]
    public void AniIcoFrameWithoutHotspotUsesDocumentedGeometricCenter()
    {
        var ico = TestCursorFiles.Cur();
        ico[2] = 1;
        ico[10] = 1; ico[11] = 0;
        ico[12] = 32; ico[13] = 0;
        using var asset = _loader.Load(TestCursorFiles.Ani([ico], [6], [0]), ".ani");
        Assert.Equal(16, asset.Frames[0].HotspotX);
        Assert.Equal(16, asset.Frames[0].HotspotY);
    }

    [Fact]
    public void Utf8BomInVectorManifestIsAcceptedAsEncodingMetadata()
    {
        var bytes = TestCursorFiles.VectorArchive("\uFEFFversion=1\nimage=\"p.svg\"\nhotspot=[4,2]", ("p.svg", TestCursorFiles.ArrowSvg));
        using var asset = _loader.Load(bytes, ".vcur");
        Assert.Equal(4, asset.Frames[0].HotspotX);
    }

    [Fact]
    public void InvalidUtf8IsRejectedEvenWhenTheBadByteIsInsideATomlComment()
    {
        using var output = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            using (var manifest = archive.CreateEntry("cursor.toml").Open())
            {
                manifest.Write(System.Text.Encoding.UTF8.GetBytes("version=1\nimage=\"p.svg\"\nhotspot=[4,2]\n# "));
                manifest.WriteByte(0xFF);
            }
            using var svg = archive.CreateEntry("p.svg").Open();
            svg.Write(System.Text.Encoding.UTF8.GetBytes(TestCursorFiles.ArrowSvg));
        }
        var error = Assert.Throws<CursorAssetException>(() => _loader.Load(output.ToArray(), ".vcur"));
        Assert.Equal(AssetErrorCode.Invalid, error.Code);
    }

    [Fact]
    public void UnwrappedSvgIsNotAUserCursorFormat()
    {
        var error = Assert.Throws<CursorAssetException>(() => _loader.Load(System.Text.Encoding.UTF8.GetBytes(TestCursorFiles.ArrowSvg), ".svg"));
        Assert.Equal(AssetErrorCode.Unsupported, error.Code);
    }
}
