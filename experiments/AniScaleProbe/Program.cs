using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;

static class Program {
    public static readonly long Origin=Stopwatch.GetTimestamp();
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,IncludeFields=true};
    [STAThread] static int Main(string[] args) {
        try {
            if(args.Length>0 && args[0]=="--restore")return RestoreWatch(args);
            if(args.Length>0 && args[0]=="--check"){using var cap=new DesktopPointerCapture();cap.Start();Thread.Sleep(300);if(cap.Failure!=null)throw cap.Failure;Console.WriteLine("DXGI_READY output="+cap.OutputBounds);return 0;}
            string root=Path.GetFullPath(args.Length>0?args[0]:Path.Combine(AppContext.BaseDirectory,"artifacts"));
            Directory.CreateDirectory(root);FixturesToDisk(root);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using var form=new ProbeForm(root);
            Application.Run(form);
            return form.ExitCode;
        }catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
    }
    static void FixturesToDisk(string root){foreach(int size in new[]{32,64})File.WriteAllBytes(Path.Combine(root,$"fixture-{size}.ani"),Fixtures.Ani(size));}
    static int RestoreWatch(string[] a){
        int pid=int.Parse(a[1]);string root=a[2], flag=Path.Combine(root,"restore-up.required");
        var sw=Stopwatch.StartNew();
        while(sw.Elapsed<TimeSpan.FromSeconds(70)){
            bool alive;try{alive=!Process.GetProcessById(pid).HasExited;}catch{alive=false;}
            if(!alive)break;
            Thread.Sleep(200);
        }
        if(File.Exists(flag)){
            try{
                var h=Native.Load(Path.Combine(root,"baseline-up.cur"),0);
                bool ok=Native.SetSystemCursor(h,32516);
                File.WriteAllText(Path.Combine(root,"watchdog-restore.json"),JsonSerializer.Serialize(new{ok,error=Marshal.GetLastWin32Error(),time=DateTimeOffset.Now},Json));
                if(ok)File.Delete(flag);return ok?0:2;
            }catch(Exception e){File.WriteAllText(Path.Combine(root,"watchdog-error.txt"),e.ToString());return 3;}
        }return 0;
    }
}
sealed record ActionSample(long Qpc,string Kind,string Detail);
sealed class CaseResult {
 public string Name=""; public int OffsetMs;public int RequestedSize;public int ActualResourceWidth;
 public long BaselineFrameQpc,BeforeQpc,AfterQpc;public int FrameBefore;public int? FirstFrameAfter,FirstWidthAfter;
 public double? TimeToFirstChangeMs,TimeToFrame3Ms,ExpectedRemainingMs;
 public string Status="";public string? Error;
}
sealed class ProbeForm : Form {
 readonly string root;readonly List<CaseResult> cases=new();readonly List<ActionSample> actions=new();readonly List<IntPtr> owned=new();
 DesktopPointerCapture? capture;IntPtr current,small,systemOriginal;bool useSystem,changedSystem;string baselineHash="";
 Native.POINT originalPosition;IntPtr originalForeground;CancellationTokenSource quit=new();
 public int ExitCode=1;
 readonly Stopwatch overall=Stopwatch.StartNew();
 long Now=>Stopwatch.GetTimestamp();
 double Ms(long delta)=>delta*1000.0/Stopwatch.Frequency;
 string Fixture(int size)=>Path.Combine(root,$"fixture-{size}.ani");
 string RestoreFlag=>Path.Combine(root,"restore-up.required");
 public ProbeForm(string root){
  this.root=root;Text="CursorShake: prueba ANI temporal (Esc cancela)";Width=650;Height=220;TopMost=true;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(30,30,30);
  Paint += (sender, args) => { using var font = new Font("Segoe UI",12); using var brush = new SolidBrush(Color.White); args.Graphics.DrawString("Prueba temporal de ANI. Esc cancela.\nSe restaura el puntero al finalizar.",font,brush,20,35); };
  KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape)quit.Cancel();};
  Shown+=async(s,e)=>await RunAsync();
  FormClosing+=(s,e)=>{if(!quit.IsCancellationRequested && ExitCode==1 && overall.ElapsedMilliseconds<70000){quit.Cancel();e.Cancel=true;}};
 }
 protected override void WndProc(ref Message m){
  if(m.Msg==0x20 && current!=IntPtr.Zero){Native.SetCursor(useSystem?Native.LoadCursorW(IntPtr.Zero,new IntPtr(32516)):current);m.Result=new IntPtr(1);return;}
  base.WndProc(ref m);
 }
 IntPtr Own(IntPtr h){if(h==IntPtr.Zero)throw new Exception("Null cursor");owned.Add(h);return h;}
 void Select(IntPtr h,string detail){
  current=h;Cursor=new Cursor(h);actions.Add(new(Now,"BeforeSetCursor",detail));
  Native.SetCursor(h);actions.Add(new(Now,"AfterSetCursor",detail));
 }
 async Task Delay(int ms){await Task.Delay(ms,quit.Token);if(capture?.Failure!=null)throw capture.Failure;}
 async Task<ShapeSample> WaitFrame(int frame,long after,int timeout=5000){
  long start=Now;
  while(Ms(Now-start)<timeout){
   quit.Token.ThrowIfCancellationRequested();
   if(capture?.Failure!=null)throw capture.Failure;
   var s=Volatile.Read(ref capture!.Latest);
   if(s!=null && s.Frame==frame && s.Qpc>after)return s;
   await Task.Delay(5,quit.Token);
  }
  throw new TimeoutException($"No visible frame {frame} after operation; last={capture!.Latest}");
 }
 async Task RunCase(string name,IntPtr replacement,int offset,bool global=false){
  var c=new CaseResult{Name=name,OffsetMs=offset,RequestedSize=64};
  cases.Add(c);
  try{
   var baseline=Own(Native.Load(Fixture(32),32));
   if(name=="same-handle-control"){replacement=baseline;c.RequestedSize=32;}
   c.ActualResourceWidth=Native.Dimensions(replacement).W;
   long start=Now;
   if(global){
    useSystem=true;InstallSystem(Native.Load(Fixture(32),32),"baseline-32");
    current=Native.LoadCursorW(IntPtr.Zero,new IntPtr(32516));Native.SetCursor(current);
   } else {useSystem=false;Select(baseline,"fresh-baseline-32:"+name);}
   await Delay(2500);
   start=Now;
   var f=await WaitFrame(2,start);
   c.BaselineFrameQpc=f.Qpc;
   await Delay(offset);
   var before=Volatile.Read(ref capture!.Latest)!;
   if(before.Frame!=2)throw new Exception("Target frame changed before replacement; timing invalid.");
   if(name=="CopyImage-request64"){replacement=Own(Native.CopyImage(baseline,2,64,64,0));c.ActualResourceWidth=Native.Dimensions(replacement).W;}
   c.FrameBefore=before.Frame;c.BeforeQpc=Now;c.ExpectedRemainingMs=700-Ms(c.BeforeQpc-c.BaselineFrameQpc);
   actions.Add(new(c.BeforeQpc,"Replace",name));
   if(global)InstallSystem(replacement,name);else Select(replacement,name);
   c.AfterQpc=Now;
   await Delay(1200);
   var post=capture.Samples.Where(s=>s.Qpc>=c.BeforeQpc).OrderBy(s=>s.Qpc).ToList();
   var first=post.FirstOrDefault();
   c.FirstFrameAfter=first?.Frame;c.FirstWidthAfter=first?.Width;
   var change=post.FirstOrDefault(s=>s.Frame>=0 && s.Frame!=2);
   var frame3=post.FirstOrDefault(s=>s.Frame==3);
   c.TimeToFirstChangeMs=change==null?null:Ms(change.Qpc-c.BeforeQpc);
   c.TimeToFrame3Ms=frame3==null?null:Ms(frame3.Qpc-c.BeforeQpc);
   c.Status="Observed";
  }catch(Exception ex){c.Status="Failed";c.Error=ex.Message;actions.Add(new(Now,"CaseError",name+": "+ex.Message));}
  finally{if(global)RestoreSystem();}
  await Delay(150);
 }
 void InstallSystem(IntPtr h,string label){
  File.WriteAllText(RestoreFlag,"baseline-up.cur");
  changedSystem=true;
  actions.Add(new(Now,"BeforeSetSystemCursor",label));
  if(!Native.SetSystemCursor(h,32516))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"SetSystemCursor");
  actions.Add(new(Now,"AfterSetSystemCursor",label));
 }
 void PrepareGlobalBackup(){
  var up=Native.LoadCursorW(IntPtr.Zero,new IntPtr(32516));
  baselineHash=Native.PixelHash(up);
  var bytes=Native.SaveStaticCursor(up);File.WriteAllBytes(Path.Combine(root,"baseline-up.cur"),bytes);
  var fromDisk=Native.Load(Path.Combine(root,"baseline-up.cur"),0);
  try{if(Native.PixelHash(fromDisk)!=baselineHash)throw new Exception("Backup does not reproduce baseline pixels; global tests prohibited.");}finally{Native.DestroyCursor(fromDisk);}
  systemOriginal=Own(Native.CopyImage(up,2,0,0,0));
  var psi=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
  psi.ArgumentList.Add("--restore");psi.ArgumentList.Add(Environment.ProcessId.ToString());psi.ArgumentList.Add(root);
  using var watchdog=Process.Start(psi);
  if(watchdog==null)throw new Exception("Could not start restore watchdog.");
 }
 void RestoreSystem(){
  if(!changedSystem)return;
  var restore=Native.CopyImage(systemOriginal,2,0,0,0);
  if(restore==IntPtr.Zero || !Native.SetSystemCursor(restore,32516))throw new Exception("System role restore failed; watchdog armed.");
  var up=Native.LoadCursorW(IntPtr.Zero,new IntPtr(32516));
  string restored=Native.PixelHash(up);
  if(restored!=baselineHash)throw new Exception("Restored role differs from baseline; watchdog remains armed.");
  changedSystem=false;useSystem=false;File.Delete(RestoreFlag);actions.Add(new(Now,"RestoredSystem","baseline pixel hash verified"));
 }
 async Task RunAsync(){
  string? failure=null;bool globalBackup=false;
  Native.GetCursorPos(out originalPosition);originalForeground=Native.GetForegroundWindow();
  try{
   capture=new DesktopPointerCapture();capture.Start();
   small=Own(Native.Load(Fixture(32),32));
   var center=PointToScreen(new Point(ClientSize.Width/2,ClientSize.Height/2));
   Native.SetCursorPos(center.X,center.Y);Select(small,"initial");
   await Delay(2800);
   if(capture.Samples.Where(s=>s.Frame>=0).Select(s=>s.Frame).Distinct().Count()!=4)throw new Exception("Control failed: four ANI frames were not observed. No phase conclusion possible.");
   await RunCase("same-handle-control",small,180);
   var fromSameFile=Own(Native.Load(Fixture(32),64));
   await RunCase("LoadImage-same-ANI-request64",fromSameFile,180);
   var fromLargeFile=Own(Native.Load(Fixture(64),64));
   await RunCase("LoadImage-prescaled-ANI64",fromLargeFile,180);
   await RunCase("LoadImage-prescaled-ANI64-offset50",Own(Native.Load(Fixture(64),64)),50);
   await RunCase("LoadImage-prescaled-ANI64-offset350",Own(Native.Load(Fixture(64),64)),350);
   var copied=Native.CopyImage(small,2,64,64,0);
   if(copied!=IntPtr.Zero)await RunCase("CopyImage-request64",Own(copied),180);
   else cases.Add(new CaseResult{Name="CopyImage-request64",Status="CopyFailed",Error="Win32="+Marshal.GetLastWin32Error()});
   try{PrepareGlobalBackup();globalBackup=true;}catch(Exception ex){cases.Add(new CaseResult{Name="system-backup",Status="Skipped",Error=ex.Message});}
   if(globalBackup){
    await RunCase("SetSystemCursor-prescaled-ANI64",Native.Load(Fixture(64),64),180,true);
   }
   ExitCode=0;
  }catch(Exception ex){failure=ex.ToString();ExitCode=1;}
  finally{
   try{RestoreSystem();}catch(Exception ex){failure=(failure??"")+"\nRESTORE: "+ex;ExitCode=2;}
   current=IntPtr.Zero;Native.SetCursor(Native.LoadCursorW(IntPtr.Zero,new IntPtr(32512)));
   Native.SetCursorPos(originalPosition.X,originalPosition.Y);
   capture?.Dispose();
   foreach(var h in owned)Native.DestroyCursor(h);
   var report=new{
    Probe="ANI phase scale experiment, not CursorShake implementation",Date=DateTimeOffset.Now,OS=Environment.OSVersion.VersionString,
    ProcessId=Environment.ProcessId,SessionId=Process.GetCurrentProcess().SessionId,QpcFrequency=Stopwatch.Frequency,
    ExpectedRatesMs=new[]{300,500,700,900},Output=capture?.OutputBounds,Failure=failure,SystemRestored=!changedSystem,
    BaselineUpPixelHash=baselineHash,Cases=cases,Actions=actions,Samples=capture?.Samples.ToArray()
   };
   File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(report,Program.Json));
   Console.WriteLine(JsonSerializer.Serialize(new{ExitCode,failure,Cases=cases,SystemRestored=!changedSystem},Program.Json));
   quit.Cancel();Close();
  }
 }
}
