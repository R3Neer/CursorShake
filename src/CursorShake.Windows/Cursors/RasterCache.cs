using CursorShake.Windows.Cursors.Formats;
using CursorShake.Windows.Cursors.Native;
namespace CursorShake.Windows.Cursors;

internal sealed class RasterCache
{
    internal const long Budget=64L*1024*1024;
    private readonly Dictionary<(CursorFrame Frame,int Width,int Height),LinkedListNode<CacheEntry>> entries=new();
    private readonly LinkedList<CacheEntry> lru=new();
    private sealed record CacheEntry((CursorFrame Frame,int Width,int Height) Key,CursorPixels Pixels);
    public long Bytes { get; private set; }
    public bool TryGet(CursorFrame frame,int width,int height,out CursorPixels pixels)
    {
        if(entries.TryGetValue((frame,width,height),out var node))
        {lru.Remove(node);lru.AddLast(node);pixels=node.Value.Pixels;return true;}
        pixels=null!;return false;
    }
    public void Reserve(long bytes,long nativeBytes)
    {
        if(bytes+nativeBytes>Budget)throw new CursorAssetException(AssetErrorCode.LimitExceeded,"Native cursor and raster cache budget exceeded.");
        while(Bytes+bytes+nativeBytes>Budget&&lru.First is{} node)
        {lru.RemoveFirst();entries.Remove(node.Value.Key);Bytes-=node.Value.Pixels.EstimatedBytes;}
    }
    public void Add(CursorFrame frame,int width,int height,CursorPixels pixels,long nativeBytes)
    {
        if(entries.ContainsKey((frame,width,height)))return;
        Reserve(pixels.EstimatedBytes,nativeBytes);var item=new CacheEntry((frame,width,height),pixels);
        var node=lru.AddLast(item);entries[item.Key]=node;Bytes+=pixels.EstimatedBytes;
    }
    public void Clear(){entries.Clear();lru.Clear();Bytes=0;}
}
