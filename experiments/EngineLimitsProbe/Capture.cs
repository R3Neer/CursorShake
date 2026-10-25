using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

sealed record DesktopAudit(long Qpc,int Width,int Height,int BluePoints,int RedPoints,int GreenPoints,bool PointerVisible,uint ShapeBytes);
sealed record ShapeSample(long Qpc, long MouseQpc, int Frame, int Width, int Height, bool Visible, double Confidence);
sealed unsafe class DesktopPointerCapture : IDisposable {
    IntPtr device, context, dxgi, adapter, output, output1, duplicate, staging;public volatile bool AuditDesktop;public readonly ConcurrentQueue<DesktopAudit> Audits=new();
    readonly CancellationTokenSource stop = new();
    Task? worker;
    public readonly ConcurrentQueue<ShapeSample> Samples = new();
    public Exception? Failure;
    public ShapeSample? Latest;
    public string OutputBounds = "";
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
    [StructLayout(LayoutKind.Sequential)] struct FrameInfo {
        public long LastPresent, LastMouse;
        public uint Frames; public int Coalesced, Protected;
        public int X,Y,Visible; public uint Metadata, ShapeBytes;
    }
    [StructLayout(LayoutKind.Sequential)] struct ShapeInfo { public uint Type, Width, Height, Pitch; public int HotX,HotY; }
    static void Check(int hr,string op) { if(hr<0) Marshal.ThrowExceptionForHR(hr, new IntPtr(-1)); }
    static IntPtr Query(IntPtr p,string iid) { Guid g=new(iid); Check(Marshal.QueryInterface(p,in g,out var q),"QI");return q; }
    static void Release(ref IntPtr p) {if(p!=IntPtr.Zero){Marshal.Release(p);p=IntPtr.Zero;}}
    public DesktopPointerCapture() {
        Check(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out device,out _,out context),"Device");
        dxgi=Query(device,"54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        var v=*(IntPtr**)dxgi;
        IntPtr a; Check(((delegate* unmanaged[Stdcall]<IntPtr,IntPtr*,int>)v[7])(dxgi,&a),"Adapter");adapter=a;
        var av=*(IntPtr**)adapter;
        byte* desc=stackalloc byte[128];
        for(uint i=0;i<16;i++){
            IntPtr o; int hr=((delegate* unmanaged[Stdcall]<IntPtr,uint,IntPtr*,int>)av[7])(adapter,i,&o);
            if(hr<0)break;

            int dhr=((delegate* unmanaged[Stdcall]<IntPtr,void*,int>)(*(IntPtr**)o)[7])(o,desc);
            int left=*(int*)(desc+64), top=*(int*)(desc+68), right=*(int*)(desc+72), bottom=*(int*)(desc+76);
            if(dhr>=0 && left<=0 && top<=0 && right>0 && bottom>0){
                output=o;OutputBounds=$"{left},{top},{right},{bottom}";break;
            }
            Marshal.Release(o);
        }
        if(output==IntPtr.Zero)throw new Exception("Primary output not found on adapter.");
        output1=Query(output,"00cddea8-939b-4b83-a340-a685226666cc");
        IntPtr dup;
        Check(((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,IntPtr*,int>)(*(IntPtr**)output1)[22])(output1,device,&dup),"DuplicateOutput");
        duplicate=dup;
    }
    [StructLayout(LayoutKind.Sequential)] struct TextureDesc {public uint W,H,Mips,Array,Format,Count,Quality,Usage,Bind,Cpu,Misc;}
    [StructLayout(LayoutKind.Sequential)] struct Mapped {public IntPtr Data;public uint RowPitch,DepthPitch;}
    void AuditResource(IntPtr resource,FrameInfo fi){
        IntPtr texture=Query(resource,"6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        try{
            TextureDesc d=default;((delegate* unmanaged[Stdcall]<IntPtr,TextureDesc*,void>)(*(IntPtr**)texture)[10])(texture,&d);
            if(staging==IntPtr.Zero){var copy=d;copy.Usage=3;copy.Bind=0;copy.Cpu=0x20000;copy.Misc=0;IntPtr st;Check(((delegate* unmanaged[Stdcall]<IntPtr,TextureDesc*,void*,IntPtr*,int>)(*(IntPtr**)device)[5])(device,&copy,null,&st),"Create staging");staging=st;}
            ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,IntPtr,void>)(*(IntPtr**)context)[47])(context,staging,texture);
            Mapped m=default;Check(((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,uint,uint,uint,Mapped*,int>)(*(IntPtr**)context)[14])(context,staging,0,1,0,&m),"Map desktop");
            int blue=0,red=0,green=0;
            try{for(int y=8;y<d.H;y+=32)for(int x=8;x<d.W;x+=32){byte* p=(byte*)m.Data+y*m.RowPitch+x*4;int r=d.Format==87?p[2]:p[0],g=p[1],b=d.Format==87?p[0]:p[2];if(b>245&&r<10&&g<10)blue++;if(r>245&&g<10&&b<10)red++;if(g>245&&r<10&&b<10)green++;}}
            finally{((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,uint,void>)(*(IntPtr**)context)[15])(context,staging,0);}
            Audits.Enqueue(new(Stopwatch.GetTimestamp(),(int)d.W,(int)d.H,blue,red,green,fi.Visible!=0,fi.ShapeBytes));
        }finally{Marshal.Release(texture);}
    }
    public void Start()=>worker=Task.Run(ReadLoop);
    void ReadLoop(){
        byte[] buffer=new byte[1024*1024];
        try {
            var vt=*(IntPtr**)duplicate;
            while(!stop.IsCancellationRequested){
                FrameInfo fi=default;IntPtr resource=IntPtr.Zero;
                int hr=((delegate* unmanaged[Stdcall]<IntPtr,uint,FrameInfo*,IntPtr*,int>)vt[8])(duplicate,20,&fi,&resource);
                if(hr==unchecked((int)0x887A0027))continue;
                Check(hr,"Acquire");
                try{
                    if(AuditDesktop)AuditResource(resource,fi);if(fi.ShapeBytes>0){
                        if(fi.ShapeBytes>68*1024*1024)throw new Exception("Pointer shape exceeds 68 MiB budget.");if(fi.ShapeBytes>buffer.Length)Array.Resize(ref buffer,(int)fi.ShapeBytes);
                        ShapeInfo si=default;uint needed=0;
                        fixed(byte* p=buffer){
                            Check(((delegate* unmanaged[Stdcall]<IntPtr,uint,void*,uint*,ShapeInfo*,int>)vt[11])(duplicate,(uint)buffer.Length,p,&needed,&si),"Shape");
                        }
                        if((si.Type==2 || si.Type==4) && si.Width>0 && si.Height>0){
                            int[] counts=new int[4];int examined=0;
                            int stride=Math.Max(1,(int)si.Width/64);for(int y=0;y<(int)si.Height;y+=stride)for(int x=0;x<(int)si.Width;x+=stride){
                                int j=y*(int)si.Pitch+x*4;
                                if(j+3>=needed)continue;
                                int b=buffer[j],g=buffer[j+1],r=buffer[j+2],alpha=buffer[j+3];
                                if(alpha<100)continue;examined++;
                                if(r>180&&g<90&&b<90)counts[0]++;
                                else if(g>180&&r<90&&b<90)counts[1]++;
                                else if(b>180&&r<90&&g<90)counts[2]++;
                                else if(r>180&&g>180&&b<90)counts[3]++;
                            }
                            int f=Array.IndexOf(counts,counts.Max());
                            double confidence=examined==0?0:(double)counts[f]/examined;
                            if(confidence<0.75)f=-1;
                            var sample=new ShapeSample(Stopwatch.GetTimestamp(),fi.LastMouse,f,(int)si.Width,(int)si.Height,fi.Visible!=0,confidence);
                            Samples.Enqueue(sample);Volatile.Write(ref Latest,sample);
                        }
                    }
                } finally {
                    if(resource!=IntPtr.Zero)Marshal.Release(resource);
                    Check(((delegate* unmanaged[Stdcall]<IntPtr,int>)vt[14])(duplicate),"ReleaseFrame");
                }
            }
        }catch(Exception ex){Failure=ex;}
    }
    public void Dispose(){
        stop.Cancel();worker?.Wait(TimeSpan.FromSeconds(3));
        Release(ref staging);Release(ref duplicate);Release(ref output1);Release(ref output);Release(ref adapter);Release(ref dxgi);Release(ref context);Release(ref device);stop.Dispose();
    }
}
