using System.Runtime.InteropServices;
using SkiaSharp;
using Svg.Skia;
using CursorShake.Windows.Cursors.Native;

namespace CursorShake.Windows.Cursors.Formats;

public enum AssetErrorCode { Invalid, Unsupported, LimitExceeded }
public sealed class CursorAssetException(AssetErrorCode code,string message,Exception? inner=null) : Exception(message,inner)
{
    public AssetErrorCode Code { get; } = code;
}

public sealed record CursorStep(int FrameIndex,TimeSpan Duration);

public sealed class CursorAsset : IDisposable
{
    public CursorAsset(IReadOnlyList<CursorFrame> frames,IReadOnlyList<CursorStep> steps)
    {
        if(frames.Count==0||steps.Count==0)throw new ArgumentException("A cursor needs a frame and a step.");
        Frames=frames;Steps=steps;Duration=TimeSpan.FromTicks(steps.Sum(x=>x.Duration.Ticks));
        EstimatedBytes=frames.Sum(x=>x.EstimatedBytes);
    }
    public IReadOnlyList<CursorFrame> Frames { get; }
    public IReadOnlyList<CursorStep> Steps { get; }
    public bool IsAnimated => Steps.Count>1;
    public TimeSpan Duration { get; }
    public long EstimatedBytes { get; }
    public int FrameIndexAt(TimeSpan elapsed)
    {
        if(!IsAnimated||Duration<=TimeSpan.Zero)return Steps[0].FrameIndex;
        long t=Math.Max(0,elapsed.Ticks)%Duration.Ticks;
        foreach(var step in Steps){if(t<step.Duration.Ticks)return step.FrameIndex;t-=step.Duration.Ticks;}
        return Steps[^1].FrameIndex;
    }
    public void Dispose(){foreach(var source in Frames.Distinct())source.Dispose();}
}

public abstract class CursorFrame : IDisposable
{
    protected CursorFrame(double width,double height,double hotspotX,double hotspotY)
    { Width=width;Height=height;HotspotX=hotspotX;HotspotY=hotspotY; }
    public double Width { get; }
    public double Height { get; }
    public double HotspotX { get; }
    public double HotspotY { get; }
    public abstract bool IsVector { get; }
    public abstract long EstimatedBytes { get; }
    public abstract CursorPixels Rasterize(int width,int height);
    public SafeCursorHandle CreateCursor(int width,int height)=>CursorNative.Create(Rasterize(width,height));
    protected (int X,int Y) HotspotAt(int width,int height)=>(Math.Clamp((int)Math.Round(HotspotX/Width*width,MidpointRounding.AwayFromZero),0,width-1),Math.Clamp((int)Math.Round(HotspotY/Height*height,MidpointRounding.AwayFromZero),0,height-1));
    protected static void ValidateSize(int width,int height)
    { if(width is <1 or >2048||height is <1 or >2048)throw new CursorAssetException(AssetErrorCode.LimitExceeded,"Raster dimensions must be between1 and2048."); }
    public abstract void Dispose();
}

internal sealed class VectorCursorFrame : CursorFrame
{
    private readonly SKSvg svg;
    private readonly long bytes;
    public VectorCursorFrame(SKSvg svg,double width,double height,double hotspotX,double hotspotY,long bytes) : base(width,height,hotspotX,hotspotY)
    {this.svg=svg;this.bytes=bytes;}
    public override bool IsVector=>true;
    public override long EstimatedBytes=>bytes;
    public override CursorPixels Rasterize(int width,int height)
    {
        ValidateSize(width,height);
        using var bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Premul));
        using(var canvas=new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)(width/Width),(float)(height/Height));
            canvas.DrawPicture(svg.Picture!);canvas.Flush();
        }
        byte[] pixels=new byte[checked(width*height*4)];Marshal.Copy(bitmap.GetPixels(),pixels,0,pixels.Length);
        var hot=HotspotAt(width,height);
        return new(width,height,hot.X,hot.Y,pixels,new byte[((width+31)/32)*4*height]);
    }
    public override void Dispose()=>svg.Dispose();
}

internal sealed class RasterCursorFrame(CursorPixels pixels) : CursorFrame(pixels.Width,pixels.Height,pixels.HotspotX,pixels.HotspotY)
{
    public override bool IsVector=>false;
    public override long EstimatedBytes=>pixels.EstimatedBytes;
    public override CursorPixels Rasterize(int width,int height)
    {
        ValidateSize(width,height);
        bool alpha=false;
        for(int i=3;i<pixels.Bgra.Length;i+=4)if(pixels.Bgra[i]!=0){alpha=true;break;}
        byte[] output=new byte[checked(width*height*4)],mask=new byte[((width+31)/32)*4*height];
        int oldStride=((pixels.Width+31)/32)*4,newStride=((width+31)/32)*4;
        if(alpha)
        {
            using var input=new SKBitmap(new SKImageInfo(pixels.Width,pixels.Height,SKColorType.Bgra8888,SKAlphaType.Premul));
            Marshal.Copy(pixels.Bgra,0,input.GetPixels(),pixels.Bgra.Length);
            using var target=new SKBitmap(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Premul));
            using(var canvas=new SKCanvas(target)){canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(input,new SKRect(0,0,width,height),new SKSamplingOptions(SKFilterMode.Linear));canvas.Flush();}
            Marshal.Copy(target.GetPixels(),output,0,output.Length);
        }
        else
        {
            // Preserve legacy AND/XOR cursors, including monochrome inversion pixels.
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int ox=Math.Min(pixels.Width-1,x*pixels.Width/width),oy=Math.Min(pixels.Height-1,y*pixels.Height/height);
                Buffer.BlockCopy(pixels.Bgra,(oy*pixels.Width+ox)*4,output,(y*width+x)*4,4);
                if((pixels.AndMask[oy*oldStride+ox/8]&(0x80>>(ox%8)))!=0)mask[y*newStride+x/8]|=(byte)(0x80>>(x%8));
            }
        }
        var hot=HotspotAt(width,height);return new(width,height,hot.X,hot.Y,output,mask);
    }
    public override void Dispose() { }
}
