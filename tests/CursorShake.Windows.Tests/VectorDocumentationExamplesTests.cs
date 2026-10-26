using System.IO.Compression;
using System.Text.RegularExpressions;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors.Formats;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class VectorDocumentationExamplesTests
{
    [Fact]
    public void PublishedStaticExampleLoadsAndKeepsItsHotspotWhenEnlarged()
    {
        var (svg, toml)=Examples();
        using var cursor=new CursorAssetLoader().Load(Package(toml[0],("pointer.svg",svg)),".vcur");
        Assert.True(cursor.Frames[0].IsVector);
        var normal=cursor.Frames[0].Rasterize(32,32);
        var large=cursor.Frames[0].Rasterize(256,256);
        Assert.Equal((2,2),(normal.HotspotX,normal.HotspotY));
        Assert.Equal((16,16),(large.HotspotX,large.HotspotY));
        var configuration=ConfigParser.Parse(toml[1]);
        Assert.Equal("my-theme",configuration.Theme);
        Assert.Equal("pointer.vcur",configuration.PersonalThemes["my-theme"].DefaultCursor);
    }
    [Fact]
    public void PublishedAnimationManifestKeepsFrameOrderAndDurations()
    {
        var (svg,toml)=Examples();
        using var cursor=new CursorAssetLoader().Load(Package(toml[3],("frame-a.svg",svg),("frame-b.svg",svg)),".vani");
        Assert.True(cursor.IsAnimated);
        Assert.All(cursor.Frames,frame=>Assert.True(frame.IsVector));
        Assert.Equal(TimeSpan.FromMilliseconds(200),cursor.Duration);
        Assert.Equal(cursor.Steps[0].FrameIndex,cursor.FrameIndexAt(TimeSpan.FromMilliseconds(79)));
        Assert.Equal(cursor.Steps[1].FrameIndex,cursor.FrameIndexAt(TimeSpan.FromMilliseconds(80)));
        Assert.Equal(cursor.Steps[0].FrameIndex,cursor.FrameIndexAt(TimeSpan.FromMilliseconds(200)));
    }
    private static (string Svg,string[] Toml) Examples()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Directory.Build.props")))root=root.Parent;
        if(root is null)throw new IOException("Repository documentation is unavailable.");
        string guide=File.ReadAllText(Path.Combine(root.FullName,"docs","CustomCursors.md"));
        string First(string language)=>Regex.Match(guide,"```"+language+"\\r?\\n([\\s\\S]*?)```").Groups[1].Value;
        return(First("xml"),Regex.Matches(guide,"```toml\\r?\\n([\\s\\S]*?)```").Select(match=>match.Groups[1].Value).ToArray());
    }
    private static byte[] Package(string manifest,params (string Name,string Text)[] images)
    {
        using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            foreach(var file in new[]{("cursor.toml",manifest)}.Concat(images))
            {using var writer=new StreamWriter(zip.CreateEntry(file.Item1).Open());writer.Write(file.Item2);}
        }
        return output.ToArray();
    }
}
