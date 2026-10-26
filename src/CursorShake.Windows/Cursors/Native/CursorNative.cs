using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CursorShake.Windows.Cursors.Native;

public sealed class SafeCursorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeCursorHandle(nint handle) : base(true) => SetHandle(handle);
    protected override bool ReleaseHandle() => CursorNative.DestroyCursor(handle);
    internal nint Transfer() { var value = handle; SetHandleAsInvalid(); return value; }
}

internal static class CursorNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct CursorInfo { public int Size, Flags; public nint Cursor; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] internal struct IconInfo { public int IsIcon; public uint HotX, HotY; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XP, YP; public uint Used, Important; }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapObject { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("user32.dll", EntryPoint="LoadCursorW", SetLastError=true)] internal static extern nint LoadCursor(nint instance, nint name);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool GetIconInfo(nint cursor, out IconInfo info);
    [DllImport("user32.dll", SetLastError=true)] internal static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool SetSystemCursor(nint cursor, uint id);
    [DllImport("user32.dll", SetLastError=true)] internal static extern nint CreateIconFromResourceEx(byte[] bytes, uint length, bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError=true)] internal static extern nint CopyImage(nint image, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyCursor(nint cursor);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", EntryPoint="GetObjectW")] internal static extern int GetObject(nint obj, int size, out BitmapObject bitmap);
    [DllImport("gdi32.dll")] internal static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bytes, nint info, uint usage);
    [DllImport("gdi32.dll", SetLastError=true)] internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError=true)] internal static extern nint CreateBitmap(int width, int height, uint planes, uint bits, byte[] data);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);

    [DllImport("user32.dll", EntryPoint="LoadImageW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern nint LoadImageFile(nint instance,string name,uint type,int width,int height,uint flags);
    internal static SafeCursorHandle LoadFile(string path,int width,int height)
    {
        var handle=LoadImageFile(0,path,2,width,height,0x10);
        if(handle==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Load cursor source: "+path);
        return new(handle);
    }

    internal static SafeCursorHandle Copy(nint source)
    {
        var h = CopyImage(source, 2, 0, 0, 0);
        if (h == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Copy cursor");
        return new(h);
    }

    internal static CursorPixels Capture(nint handle)
    {
        if (!GetIconInfo(handle, out var icon)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Read cursor");
        var dc = GetDC(0);
        try
        {
            var bitmap = icon.Color != 0 ? icon.Color : icon.Mask;
            if (GetObject(bitmap, Marshal.SizeOf<BitmapObject>(), out var obj) == 0) throw new Win32Exception();
            int width = obj.Width, height = icon.Color == 0 ? obj.Height / 2 : obj.Height;
            if (width is <1 or >2048 || height is <1 or >2048) throw new InvalidDataException("Cursor dimensions exceed 2048 pixels.");
            int stride = ((width + 31) / 32) * 4;
            byte[] pixels = new byte[checked(width * height * 4)];
            byte[] masks = new byte[checked(stride * height * (icon.Color == 0 ? 2 : 1))];
            var info = Marshal.AllocHGlobal(48);
            try
            {
                if (icon.Color != 0)
                {
                    Marshal.StructureToPtr(new BitmapInfo { Size=40,Width=width,Height=-height,Planes=1,BitCount=32 }, info, false);
                    if (GetDIBits(dc, icon.Color, 0, (uint)height, pixels, info, 0) != height) throw new Win32Exception();
                }
                int maskHeight = icon.Color == 0 ? height * 2 : height;
                Marshal.StructureToPtr(new BitmapInfo { Size=40,Width=width,Height=-maskHeight,Planes=1,BitCount=1 }, info, false);
                Marshal.WriteInt32(info,40,0); Marshal.WriteInt32(info,44,0x00ffffff);
                if (GetDIBits(dc, icon.Mask, 0, (uint)maskHeight, masks, info, 0) != maskHeight) throw new Win32Exception();
                if (icon.Color == 0)
                    for (int y=0; y<height; y++) for (int x=0; x<width; x++)
                    {
                        byte value = (masks[(y+height)*stride+x/8] & (0x80>>(x%8))) != 0 ? (byte)255 : (byte)0;
                        int k=(y*width+x)*4; pixels[k]=pixels[k+1]=pixels[k+2]=value;
                    }
            }
            finally { Marshal.FreeHGlobal(info); }
            return new(width,height,(int)icon.HotX,(int)icon.HotY,pixels,masks[..(stride*height)]);
        }
        finally { ReleaseDC(0,dc); if(icon.Color!=0)DeleteObject(icon.Color); if(icon.Mask!=0)DeleteObject(icon.Mask); }
    }

    internal static SafeCursorHandle Create(CursorPixels pixels)
    {
        var info = new BitmapInfo { Size=40,Width=pixels.Width,Height=-pixels.Height,Planes=1,BitCount=32 };
        var color=CreateDIBSection(0,ref info,0,out var data,0,0);
        // CreateBitmap's monochrome scanlines are WORD aligned, unlike DIB's DWORD alignment.
        int dibStride=((pixels.Width+31)/32)*4, ddbStride=((pixels.Width+15)/16)*2;
        byte[] maskData=new byte[ddbStride*pixels.Height];
        for(int y=0;y<pixels.Height;y++) Buffer.BlockCopy(pixels.AndMask,y*dibStride,maskData,y*ddbStride,ddbStride);
        var mask=CreateBitmap(pixels.Width,pixels.Height,1,1,maskData);
        try
        {
            if(color==0||mask==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Create cursor bitmap");
            Marshal.Copy(pixels.Bgra,0,data,pixels.Bgra.Length);
            var icon=new IconInfo { HotX=(uint)pixels.HotspotX,HotY=(uint)pixels.HotspotY,Color=color,Mask=mask };
            var cursor=CreateIconIndirect(ref icon);
            if(cursor==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Create cursor");
            return new(cursor);
        }
        finally { if(color!=0)DeleteObject(color);if(mask!=0)DeleteObject(mask); }
    }
}

public sealed record CursorPixels(int Width,int Height,int HotspotX,int HotspotY,byte[] Bgra,byte[] AndMask)
{
    public long EstimatedBytes => Bgra.LongLength+AndMask.LongLength;
}
