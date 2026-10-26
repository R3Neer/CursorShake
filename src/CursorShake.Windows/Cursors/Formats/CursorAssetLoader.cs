using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Globalization;
using Svg.Skia;
using Tomlyn;
using Tomlyn.Model;
using CursorShake.Windows.Cursors.Native;

namespace CursorShake.Windows.Cursors.Formats;

public sealed partial class CursorAssetLoader
{
    public const int MaximumFileBytes=16*1024*1024, MaximumSvgBytes=4*1024*1024, MaximumExpandedBytes=32*1024*1024, MaximumFrames=256;
    public const long MaximumSourceBytes=64L*1024*1024;
    public CursorAsset Load(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>MaximumFileBytes)throw Limit("Resource exceeds16MiB.");
        using var output=new MemoryStream((int)stream.Length);
        byte[] buffer=new byte[8192];int count;
        while((count=stream.Read(buffer))!=0)
        {
            if(output.Length+count>MaximumFileBytes)throw Limit("Resource exceeds16MiB.");
            output.Write(buffer,0,count);
        }
        return Load(output.ToArray(),Path.GetExtension(path));
    }
    public CursorAsset Load(byte[] data,string extension)
    {
        if(data.Length>MaximumFileBytes)throw Limit("Resource exceeds16MiB.");
        try{return extension.ToLowerInvariant() switch
        {
            ".vcur"=>LoadVector(data,false),".vani"=>LoadVector(data,true),
            ".cur"=>LoadCur(data),".ani"=>LoadAni(data),
            _=>throw new CursorAssetException(AssetErrorCode.Unsupported,"Supported cursor formats: CUR, ANI, VCUR, VANI.")
        };}
        catch(CursorAssetException){throw;}
        catch(Exception ex) when(ex is InvalidDataException or EndOfStreamException or OverflowException or ArgumentException or XmlException or FormatException)
        {throw new CursorAssetException(AssetErrorCode.Invalid,"Invalid cursor resource: "+ex.Message,ex);}
    }
    private static CursorAssetException Invalid(string s)=>new(AssetErrorCode.Invalid,s);
    private static CursorAssetException Limit(string s)=>new(AssetErrorCode.LimitExceeded,s);
    private static ushort U16(byte[] b,int p)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p,2));
    private static uint U32(byte[] b,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p,4));
    private static void CheckKeys(TomlTable table,params string[] allowed)
    {foreach(var key in table.Keys)if(!allowed.Contains(key,StringComparer.Ordinal))throw Invalid("Unknown field: "+key);}
    private static string String(TomlTable table,string key)=>table.TryGetValue(key,out var value)&&value is string s&&!string.IsNullOrWhiteSpace(s)?s:throw Invalid("Missing string: "+key);
    private static double Number(object? v)=>v switch { long l=>l,double d when double.IsFinite(d)=>d,_=>throw Invalid("Expected finite number.") };
    private static (double X,double Y)? Hotspot(TomlTable table)
    {
        if(!table.TryGetValue("hotspot",out var v))return null;
        if(v is not TomlArray a||a.Count!=2)throw Invalid("hotspot must contain two numbers.");
        return(Number(a[0]),Number(a[1]));
    }
    private static string EntryName(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Contains('\\')||name.Contains(':')||name.StartsWith('/')||name.Split('/').Any(x=>x is "" or "." or ".."))throw Invalid("Unsafe archive path: "+name);
        return name;
    }
    private CursorAsset LoadVector(byte[] data,bool animated)
    {
        using var archive=new ZipArchive(new MemoryStream(data),ZipArchiveMode.Read);
        if(archive.Entries.Count>MaximumFrames+1)throw Limit("Too many archive entries.");
        long expanded=0;var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in archive.Entries)
        {
            var name=EntryName(e.FullName);
            if(((e.ExternalAttributes>>16)&0xf000)==0xa000)throw Invalid("Archive links are not supported.");
            expanded=checked(expanded+e.Length);if(expanded>MaximumExpandedBytes)throw Limit("Expanded archive exceeds32MiB.");
            if(!entries.TryAdd(name,e))throw Invalid("Duplicate archive path: "+name);
            if(name!="cursor.toml"&&!name.EndsWith(".svg",StringComparison.OrdinalIgnoreCase))throw Invalid("Unexpected archive entry: "+name);
            if(e.Length>(name=="cursor.toml"?65536:MaximumSvgBytes))throw Limit("Archive entry exceeds limit.");
        }
        if(!entries.TryGetValue("cursor.toml",out var manifest))throw Invalid("Missing cursor.toml.");
        var text=new UTF8Encoding(false,true).GetString(ReadEntry(manifest,65536));
        if(text.StartsWith('\uFEFF'))text=text[1..];
        var syntax=Toml.Parse(text);if(syntax.HasErrors)throw Invalid(syntax.Diagnostics.ToString());
        var model=Toml.ToModel(text);CheckKeys(model,animated?["version","hotspot","frames"]:["version","image","hotspot"]);
        if(!model.TryGetValue("version",out var version)||version is not long v||v!=1)throw new CursorAssetException(AssetErrorCode.Unsupported,"Unsupported or missing cursor format version; expected1.");
        var commonHotspot=Hotspot(model);var sources=new List<CursorFrame>();var steps=new List<CursorStep>();
        var sourceIndices=new Dictionary<string,int>(StringComparer.Ordinal);
        try
        {
            void AddFrame(TomlTable frame,TimeSpan duration,(double X,double Y)? inherited)
            {
                string name=EntryName(String(frame,"image"));
                if(!entries.TryGetValue(name,out var entry)||!name.EndsWith(".svg",StringComparison.OrdinalIgnoreCase))throw Invalid("Missing SVG: "+name);
                var hot=Hotspot(frame)??inherited??throw Invalid("Missing cursor hotspot.");
                string key=name+"|"+hot.X.ToString("R",CultureInfo.InvariantCulture)+"|"+hot.Y.ToString("R",CultureInfo.InvariantCulture);
                if(!sourceIndices.TryGetValue(key,out int index))
                {
                    index=sources.Count;sources.Add(ReadSvg(ReadEntry(entry,MaximumSvgBytes),hot.X,hot.Y));sourceIndices[key]=index;
                    if(sources.Sum(x=>x.EstimatedBytes)>MaximumSourceBytes)throw Limit("Retained source budget exceeded.");
                }
                steps.Add(new(index,duration));
            }
            if(animated)
            {
                if(!model.TryGetValue("frames",out var frames)||frames is not TomlTableArray array||array.Count is <1 or >MaximumFrames)throw Invalid("VANI needs1–256 frames.");
                foreach(var frame in array)
                {
                    CheckKeys(frame,"image","duration_ms","hotspot");
                    if(!frame.TryGetValue("duration_ms",out var ms))throw Invalid("Missing duration_ms.");
                    double duration=Number(ms);if(duration<=0||duration>86400000)throw Invalid("Frame duration must be positive and at most one day.");
                    var effectiveDuration=TimeSpan.FromMilliseconds(duration);
                    if(effectiveDuration<=TimeSpan.Zero)throw Invalid("Frame duration is below clock resolution.");
                    AddFrame(frame,effectiveDuration,commonHotspot);
                }
            }
            else AddFrame(model,TimeSpan.Zero,null);
            return new(sources,steps);
        }
        catch{foreach(var source in sources)source.Dispose();throw;}
    }
    private static byte[] ReadEntry(ZipArchiveEntry entry,int maximum)
    {
        using var stream=entry.Open();using var output=new MemoryStream();byte[] buffer=new byte[8192];int count;
        while((count=stream.Read(buffer))!=0){if(output.Length+count>maximum)throw Limit("Archive entry expands beyond limit.");output.Write(buffer,0,count);}
        if(output.Length!=entry.Length)throw Invalid("Archive entry length mismatch.");return output.ToArray();
    }
    private static CursorFrame ReadSvg(byte[] bytes,double hotX,double hotY)
    {
        using var xml=XmlReader.Create(new MemoryStream(bytes),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumSvgBytes });
        var doc=XDocument.Load(xml);var root=doc.Root??throw Invalid("Missing SVG root.");
        if(root.Name.LocalName!="svg")throw Invalid("Expected SVG root.");
        foreach(var node in root.DescendantsAndSelf())
        {
            if(node.Name.LocalName is "script" or "foreignObject" or "animate" or "animateTransform" or "animateMotion" or "set" or "image")throw Invalid("Executable, animated or embedded images are not supported.");
            foreach(var attribute in node.Attributes())
            {
                string value=attribute.Value;
                if(attribute.Name.LocalName.StartsWith("on",StringComparison.OrdinalIgnoreCase))throw Invalid("SVG event handlers are not supported.");
                if(attribute.Name.LocalName=="style"&&value.Contains('\\'))throw Invalid("Escaped SVG CSS is not supported.");
                if(attribute.Name.LocalName=="href"&&!value.StartsWith('#'))throw Invalid("External SVG references are not supported.");
                foreach(Match m in Regex.Matches(value,@"url\(\s*['""\s]*([^)'""\s]+)",RegexOptions.IgnoreCase))if(!m.Groups[1].Value.StartsWith('#'))throw Invalid("External SVG URL.");
            }
            if(node.Name.LocalName=="style")
            {
                if(node.Value.Contains('\\')||Regex.IsMatch(node.Value,"@import",RegexOptions.IgnoreCase))throw Invalid("Imported or escaped SVG CSS is not supported.");
                foreach(Match m in Regex.Matches(node.Value,@"url\(\s*['""\s]*([^)'""\s]+)",RegexOptions.IgnoreCase))
                    if(!m.Groups[1].Value.StartsWith('#'))throw Invalid("External SVG CSS references are not supported.");
            }
        }
        var parts=((string?)root.Attribute("viewBox")??"").Split([' ',',','\t','\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length!=4)throw Invalid("SVG requires viewBox.");
        var view=parts.Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();
        if(view.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1000000)||view[2]<=0||view[3]<=0)throw Invalid("Invalid SVG viewBox.");
        if(hotX<view[0]||hotY<view[1]||hotX>=view[0]+view[2]||hotY>=view[1]+view[3])throw Invalid("Hotspot outside SVG viewBox.");
        // Normalize viewport to viewBox proportions: all formats use the full SVG canvas.
        root.SetAttributeValue("width",view[2].ToString("R",CultureInfo.InvariantCulture));root.SetAttributeValue("height",view[3].ToString("R",CultureInfo.InvariantCulture));
        root.SetAttributeValue("preserveAspectRatio","xMinYMin meet");
        var svg=new SKSvg();
        try
        {
            using var stream=new MemoryStream(Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting)));
            if(svg.Load(stream)==null||svg.Picture==null)throw Invalid("Unable to rasterize SVG.");
            long estimated=bytes.LongLength+svg.Picture.ApproximateBytesUsed;
            return new VectorCursorFrame(svg,view[2],view[3],hotX-view[0],hotY-view[1],estimated);
        }
        catch{svg.Dispose();throw;}
    }
    private CursorAsset LoadCur(byte[] data,bool allowIcon=false)
    {
        if(data.Length<22||U16(data,0)!=0||(U16(data,2)!=2&&!(allowIcon&&U16(data,2)==1)))throw Invalid("Invalid CUR header.");
        bool icon=U16(data,2)==1;
        int count=U16(data,4);if(count is <1 or >MaximumFrames||6+count*16>data.Length)throw Invalid("Invalid CUR image count.");
        int best=-1,area=-1;
        for(int i=0;i<count;i++)
        {
            int p=6+i*16,width=data[p]==0?256:data[p],height=data[p+1]==0?256:data[p+1];
            uint length=U32(data,p+8),offset=U32(data,p+12);
            if(length<8||offset<6+count*16||(ulong)offset+length>(ulong)data.Length)throw Invalid("Invalid CUR image bounds.");
            if(!icon&&(U16(data,p+4)>=width||U16(data,p+6)>=height))throw Invalid("CUR hotspot outside image.");
            if(width*height>area){best=p;area=width*height;}
        }
        uint size=U32(data,best+8),start=U32(data,best+12);int w=data[best]==0?256:data[best],h=data[best+1]==0?256:data[best+1];
        byte[] image=data.AsSpan((int)start,(int)size).ToArray();
        // Validate embedded PNG/DIB dimensions before allowing Win32 to allocate.
        if(image.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}))
        {
            if(image.Length<24||BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(16,4))!=w||BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(20,4))!=h)throw Invalid("PNG dimensions disagree with CUR directory.");
        }
        else if(image.Length<40||U32(image,0)<40||BitConverter.ToInt32(image,4)!=w||Math.Abs((long)BitConverter.ToInt32(image,8))!=h*2)throw Invalid("Unsupported or mismatched CUR bitmap dimensions.");
        byte[] resource=new byte[image.Length+4];
        if(icon){BinaryPrimitives.WriteUInt16LittleEndian(resource.AsSpan(0,2),(ushort)(w/2));BinaryPrimitives.WriteUInt16LittleEndian(resource.AsSpan(2,2),(ushort)(h/2));}
        else Buffer.BlockCopy(data,best+4,resource,0,4);
        Buffer.BlockCopy(image,0,resource,4,image.Length);
        using var handle=new SafeCursorHandle(CursorNative.CreateIconFromResourceEx(resource,(uint)resource.Length,false,0x30000,w,h,0));
        if(handle.IsInvalid)throw Invalid("Windows could not decode CUR.");
        var frame=new RasterCursorFrame(CursorNative.Capture(handle.DangerousGetHandle()));
        return new([frame],[new CursorStep(0,TimeSpan.Zero)]);
    }
    private CursorAsset LoadAni(byte[] data)
    {
        if(data.Length<12||Encoding.ASCII.GetString(data,0,4)!="RIFF"||Encoding.ASCII.GetString(data,8,4)!="ACON"||(ulong)U32(data,4)+8!=(ulong)data.Length)throw Invalid("Invalid ANI container.");
        byte[]? header=null,rates=null,sequence=null;var rawFrames=new List<byte[]>();
        void Chunks(int begin,int end,bool frames)
        {
            int at=begin;
            while(at<end)
            {
                if(end-at<8)throw Invalid("Truncated ANI chunk.");
                string id=Encoding.ASCII.GetString(data,at,4);uint length=U32(data,at+4);long next=(long)at+8+length+(length&1);
                if(next>end)throw Invalid("ANI chunk outside container.");int content=at+8;
                if(frames&&id=="icon") { if(rawFrames.Count>=MaximumFrames)throw Limit("Too many ANI frames.");rawFrames.Add(data.AsSpan(content,(int)length).ToArray()); }
                else if(!frames)
                {
                    switch(id)
                    {
                        case "anih": if(header!=null)throw Invalid("Duplicate ANI header.");header=data.AsSpan(content,(int)length).ToArray();break;
                        case "rate": if(rates!=null)throw Invalid("Duplicate ANI rates.");rates=data.AsSpan(content,(int)length).ToArray();break;
                        case "seq ": if(sequence!=null)throw Invalid("Duplicate ANI sequence.");sequence=data.AsSpan(content,(int)length).ToArray();break;
                        case "LIST": if(length<4)throw Invalid("Invalid ANI list.");if(Encoding.ASCII.GetString(data,content,4)=="fram")Chunks(content+4,content+(int)length,true);break;
                    }
                }
                at=(int)next;
            }
        }
        Chunks(12,data.Length,false);
        if(header is null||header.Length<36||U32(header,0)!=36)throw Invalid("Missing ANI header.");
        uint frameCount=U32(header,4),stepCount=U32(header,8),defaultRate=U32(header,28),flags=U32(header,32);
        if(frameCount is <1 or >MaximumFrames||stepCount is <1 or >MaximumFrames)throw Limit("ANI frame/step limit exceeded.");
        if((flags&1)==0)throw new CursorAssetException(AssetErrorCode.Unsupported,"Raw bitmap ANI frames are not supported; use CUR resources.");
        if(frameCount!=rawFrames.Count)throw Invalid("ANI frame count mismatch.");
        if((flags&2)!=0&&(sequence==null||sequence.Length!=stepCount*4))throw Invalid("Invalid ANI sequence.");
        if(rates!=null&&rates.Length!=stepCount*4)throw Invalid("Invalid ANI rates.");
        var sources=new List<CursorFrame>();var steps=new List<CursorStep>();
        try
        {
            foreach(var raw in rawFrames)
            {
                using var asset=LoadCur(raw,true);var pixels=asset.Frames[0].Rasterize((int)asset.Frames[0].Width,(int)asset.Frames[0].Height);sources.Add(new RasterCursorFrame(pixels));
                if(sources.Sum(x=>x.EstimatedBytes)>MaximumSourceBytes)throw Limit("ANI source budget exceeded.");
            }
            for(int i=0;i<stepCount;i++)
            {
                uint index=(flags&2)!=0?U32(sequence!,i*4):(uint)i;
                if(index>=frameCount)throw Invalid("ANI sequence index out of bounds.");
                uint rate=rates==null?defaultRate:U32(rates,i*4);if(rate is 0 or >5184000)throw Invalid("ANI frame duration out of range.");
                steps.Add(new((int)index,TimeSpan.FromTicks(checked((long)rate*TimeSpan.TicksPerSecond/60))));
            }
            return new(sources,steps);
        }
        catch{foreach(var source in sources)source.Dispose();throw;}
    }
}
