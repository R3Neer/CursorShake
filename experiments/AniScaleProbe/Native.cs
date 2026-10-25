using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using System.Security.Cryptography;
static class Native {
 [StructLayout(LayoutKind.Sequential)] public struct POINT {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] public struct ICONINFO {public int IsIcon;public uint HotX,HotY;public IntPtr Mask,Color;}
 [StructLayout(LayoutKind.Sequential)] public struct BITMAP {public int Type,Width,Height,WidthBytes;public ushort Planes,BitsPixel;public IntPtr Bits;}
 [StructLayout(LayoutKind.Sequential)] public struct BMI {public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,ImageSize;public int XP,YP;public uint Used,Important;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr LoadImageW(IntPtr inst,string name,uint type,int x,int y,uint flags);
 [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr SetCursor(IntPtr cursor);
 [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr CopyImage(IntPtr image,uint type,int x,int y,uint flags);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool SetSystemCursor(IntPtr cursor,uint id);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetIconInfo(IntPtr cursor,out ICONINFO info);
 [DllImport("user32.dll")] public static extern IntPtr LoadCursorW(IntPtr instance,IntPtr name);
 [DllImport("user32.dll")] public static extern bool DestroyCursor(IntPtr h);
 [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int w,int h,uint frame,IntPtr brush,uint flags);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
 [DllImport("gdi32.dll")] public static extern int GetObjectW(IntPtr obj,int n,out BITMAP bitmap);
 [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
 [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint start,uint lines,byte[] bits,ref BMI info,uint usage);
 public static IntPtr Load(string path,int size) {
   var h=LoadImageW(IntPtr.Zero,path,2,size,size,0x10);
   if(h==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"LoadImage "+path);return h;
 }
 public static (int W,int H,int X,int Y) Dimensions(IntPtr h) {
   if(!GetIconInfo(h,out var i))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   try{GetObjectW(i.Color!=IntPtr.Zero?i.Color:i.Mask,Marshal.SizeOf<BITMAP>(),out var b);return(b.Width,i.Color==IntPtr.Zero?b.Height/2:b.Height,(int)i.HotX,(int)i.HotY);}
   finally{if(i.Color!=IntPtr.Zero)DeleteObject(i.Color);if(i.Mask!=IntPtr.Zero)DeleteObject(i.Mask);}
 }
 public static string PixelHash(IntPtr h) {
   var d=Dimensions(h);using var b=new Bitmap(d.W,d.H,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(b)){g.Clear(Color.Transparent);var dc=g.GetHdc();try{if(!DrawIconEx(dc,0,0,h,d.W,d.H,0,IntPtr.Zero,3))throw new Exception("DrawIconEx");}finally{g.ReleaseHdc(dc);}}
   var bd=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{byte[] a=new byte[bd.Stride*b.Height];Marshal.Copy(bd.Scan0,a,0,a.Length);return Convert.ToHexString(SHA256.HashData(a));}finally{b.UnlockBits(bd);}
 }
 public static byte[] SaveStaticCursor(IntPtr h) {
   if(!GetIconInfo(h,out var i))throw new Exception("GetIconInfo baseline");
   var dc=GetDC(IntPtr.Zero);
   try{
    if(i.Color==IntPtr.Zero)throw new Exception("Baseline cursor monochrome not supported by snapshot writer; no system mutation permitted.");
    GetObjectW(i.Color,Marshal.SizeOf<BITMAP>(),out var b);
    int w=b.Width,ht=b.Height,maskStride=((w+31)/32)*4;
    byte[] pixels=new byte[w*ht*4], mask=new byte[maskStride*ht];
    BMI color=new(){Size=40,Width=w,Height=ht,Planes=1,BitCount=32};
    if(GetDIBits(dc,i.Color,0,(uint)ht,pixels,ref color,0)!=(int)ht)throw new Exception("Snapshot color");
    // GetDIBits with 1bpp needs space for its two palette entries.
    byte[] maskWithPalette=new byte[48]; // Native call below writes BITMAPINFO; use manual buffer.
    IntPtr bmi=Marshal.AllocHGlobal(48);
    try{
      BMI mi=new(){Size=40,Width=w,Height=ht,Planes=1,BitCount=1};
      Marshal.StructureToPtr(mi,bmi,false);
      Marshal.WriteInt32(bmi,40,0);Marshal.WriteInt32(bmi,44,0x00FFFFFF);
      if(GetDIBitsRaw(dc,i.Mask,0,(uint)ht,mask,bmi,0)!=(int)ht)throw new Exception("Snapshot mask");
    }finally{Marshal.FreeHGlobal(bmi);}
    return Fixtures.Cur(w,ht,(int)i.HotX,(int)i.HotY,pixels,mask);
   }finally{ReleaseDC(IntPtr.Zero,dc);DeleteObject(i.Color);DeleteObject(i.Mask);}
 }
 [DllImport("gdi32.dll",EntryPoint="GetDIBits")] static extern int GetDIBitsRaw(IntPtr dc,IntPtr bitmap,uint start,uint lines,byte[] bits,IntPtr info,uint usage);
}
