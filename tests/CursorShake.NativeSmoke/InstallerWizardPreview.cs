using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
namespace CursorShake.NativeSmoke;

/// <summary>Test-only preview of an owned installer. It never sends Install/Finish or global input.</summary>
internal static class InstallerWizardPreview
{
    public static int Run(string candidateExe,string reportDirectory)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        candidateExe=Path.GetFullPath(candidateExe);reportDirectory=Path.GetFullPath(reportDirectory);Directory.CreateDirectory(reportDirectory);
        if(!File.Exists(candidateExe)||!candidateExe.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("An existing installer executable is required.");
        string target=Path.Combine(reportDirectory,"preview-target-"+Guid.NewGuid().ToString("N"));
        var pages=new List<object>();var errors=new List<string>();bool reachedReady=false,cancelled=false;int? exitCode=null;
        string before=RegistrySnapshot();string sha;
        using var candidate=new FileStream(candidateExe,FileMode.Open,FileAccess.Read,FileShare.Read);
        sha=Convert.ToHexString(SHA256.HashData(candidate));
        var start=new ProcessStartInfo(candidateExe){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(candidateExe)!};
        foreach(string argument in new[]{"/CURRENTUSER","/SP-","/LANG=spanish","/NORESTART","/DIR="+target})start.ArgumentList.Add(argument);
        using var installer=Process.Start(start)??throw new IOException("Installer did not start.");
        var owned=new OwnedWindows(installer);nint wizard=0;
        try
        {
            wizard=WaitWindow(owned,TimeSpan.FromSeconds(20),w=>Class(w)=="TWizardForm");
            string? lastPage=null;
            for(int step=0;step<12;step++)
            {
                Thread.Sleep(300);owned.Verify(wizard);var controls=Controls(wizard,owned);string signature=Signature(controls);
                if(signature==lastPage)throw new IOException("Wizard page did not advance; no further click was sent.");lastPage=signature;
                string stage=Stage(controls);string name=$"{step:00}-{stage}.png";
                Capture(wizard,Path.Combine(reportDirectory,name),owned);pages.Add(new{Step=step,Stage=stage,Image=name,Controls=controls});
                var install=controls.FirstOrDefault(c=>c.Visible&&c.Enabled&&IsInstall(c.Text));
                if(install is not null){reachedReady=true;break;}
                if(controls.Any(c=>c.Visible&&c.Enabled&&IsFinish(c.Text)))throw new IOException("Unexpected Finish page: installation was never authorized by this preview.");
                var next=controls.Where(c=>c.Visible&&c.Enabled&&IsNext(c.Text)&&IsButton(c.ClassName)).ToArray();
                if(next.Length!=1)throw new IOException("Cannot uniquely identify this wizard's Next button; no click was sent.");
                Click(wizard,next[0],owned);
                WaitForChange(owned,wizard,signature,TimeSpan.FromSeconds(5));
            }
            if(!reachedReady)throw new IOException("Preview did not reach Ready to Install within its bounded page count.");
        }
        catch(Exception error){errors.Add(error.ToString());}
        finally
        {
            try{cancelled=CancelOwned(owned,wizard);if(installer.WaitForExit(15000))exitCode=installer.ExitCode;else errors.Add("Owned installer remains alive after cancellation timeout; it was not forcibly terminated.");}
            catch(Exception error){errors.Add("Cancellation: "+error);}
        }
        string after=RegistrySnapshot();bool registryUnchanged=before==after,targetAbsent=!Directory.Exists(target)&&!File.Exists(target);
        bool success=reachedReady&&cancelled&&exitCode is not null&&exitCode!=0&&registryUnchanged&&targetAbsent&&errors.Count==0;
        var report=new{Passed=success,Candidate=candidateExe,Sha256=sha,InstallerPid=installer.Id,PrivateTarget=target,ReachedReadyWithoutInstalling=reachedReady,Cancelled=cancelled,ExitCode=exitCode,RegistryUnchanged=registryUnchanged,TargetAbsent=targetAbsent,RegistryBefore=JsonSerializer.Deserialize<JsonElement>(before),RegistryAfter=JsonSerializer.Deserialize<JsonElement>(after),Pages=pages,Errors=errors,
            Scope="Only child windows belonging to this launched installer tree; PrintWindow captures no desktop. Navigation never sends Install/Finish; cancellation uses owned Cancel/Yes only."};
        File.WriteAllText(Path.Combine(reportDirectory,"wizard-preview.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        return success?0:1;
    }
    private static bool CancelOwned(OwnedWindows owned,nint wizard)
    {
        if(owned.Root.HasExited)return true;
        if(wizard==0)wizard=owned.TopLevel().FirstOrDefault(w=>Class(w)=="TWizardForm");
        if(wizard!=0)
        {
            var cancel=Controls(wizard,owned).Where(c=>c.Visible&&c.Enabled&&IsCancel(c.Text)&&IsButton(c.ClassName)).ToArray();
            if(cancel.Length!=1)throw new IOException("Owned Cancel button cannot be uniquely identified; no blind window-close was sent.");
            Click(wizard,cancel[0],owned);
        }
        else throw new IOException("Owned wizard could not be found for normal cancellation.");
        var deadline=Stopwatch.StartNew();
        while(deadline.Elapsed<TimeSpan.FromSeconds(10)&&!owned.Root.HasExited)
        {
            foreach(nint window in owned.TopLevel().Where(w=>w!=wizard))
            {
                var controls=Controls(window,owned);
                var yes=controls.Where(c=>c.Visible&&c.Enabled&&IsYes(c.Text)&&IsButton(c.ClassName)).ToArray();
                bool hasNo=controls.Any(c=>c.Visible&&c.Enabled&&Normalize(c.Text)=="no");
                if(yes.Length==1&&hasNo)Click(window,yes[0],owned);
            }
            Thread.Sleep(100);
        }
        return owned.Root.HasExited;
    }
    private static void WaitForChange(OwnedWindows owned,nint wizard,string old,TimeSpan timeout)
    {
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<timeout)
        {
            if(owned.Root.HasExited)throw new IOException("Installer exited during preview navigation.");
            if(Signature(Controls(wizard,owned))!=old)return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("Owned wizard did not change after Next.");
    }
    private static nint WaitWindow(OwnedWindows owned,TimeSpan timeout,Func<nint,bool> predicate)
    {
        var timer=Stopwatch.StartNew();while(timer.Elapsed<timeout)
        {
            if(owned.Root.HasExited)throw new IOException("Installer exited before showing its wizard.");
            var window=owned.TopLevel().FirstOrDefault(predicate);if(window!=0)return window;Thread.Sleep(100);
        }
        throw new TimeoutException("Owned installer wizard was not found.");
    }
    private static List<ControlInfo> Controls(nint window,OwnedWindows owned)
    {
        owned.Verify(window);var result=new List<ControlInfo>();Exception? failure=null;
        EnumChildWindows(window,(child,_)=>
        {
            try{owned.Verify(child);GetWindowRect(child,out var rect);result.Add(new((long)child,GetDlgCtrlID(child),Class(child),Text(child),IsWindowVisible(child),IsWindowEnabled(child),rect));return true;}
            catch(Exception error){failure=error;return false;}
        },0);
        if(failure is not null)throw new IOException("Owned control enumeration could not be verified.",failure);
        return result;
    }
    private static string Signature(IEnumerable<ControlInfo> controls)=>string.Join("|",controls.Where(c=>c.Visible).Select(c=>$"{c.Id}:{c.Text}:{c.Enabled}"));
    private static string Stage(List<ControlInfo> controls)
    {
        if(controls.Any(c=>c.Visible&&IsInstall(c.Text)))return "ready";
        string text=string.Join(" ",controls.Where(c=>c.Visible).Select(c=>Normalize(c.Text)));
        if(text.Contains("bienvenido")||text.Contains("welcome"))return "welcome";
        if(text.Contains("tareas adicionales")||text.Contains("additional tasks"))return "tasks";
        if(text.Contains("carpeta de destino")||text.Contains("destination location"))return "destination";
        return "wizard-page";
    }
    private static void Click(nint window,ControlInfo button,OwnedWindows owned)
    {
        nint handle=(nint)button.Handle;owned.Verify(window);owned.Verify(handle);
        if(GetAncestor(handle,2)!=window||!IsWindowVisible(handle)||!IsWindowEnabled(handle)||!IsButton(Class(handle)))throw new IOException("Owned button identity changed before navigation.");
        string text=Text(handle);
        if(IsInstall(text)||IsFinish(text))throw new InvalidOperationException("Preview is prohibited from invoking Install or Finish.");
        if(!(IsNext(text)||IsCancel(text)||IsYes(text)))throw new IOException("Button is outside the preview's navigation allowlist.");
        _=SendMessageTimeout(handle,0x00F5,0,0,0x0003,1000,out _); // BM_CLICK; modal cancellation can keep the synchronous handler busy.
    }
    private static string Normalize(string value)=>value.Replace("&","").Replace(">","").Trim().ToLowerInvariant();
    private static bool IsNext(string text)=>Normalize(text) is "siguiente" or "next";
    private static bool IsInstall(string text)=>Normalize(text) is "instalar" or "install";
    private static bool IsFinish(string text)=>Normalize(text) is "finalizar" or "finish";
    private static bool IsCancel(string text)=>Normalize(text) is "cancelar" or "cancel";
    private static bool IsYes(string text)=>Normalize(text) is "sí" or "si" or "yes";
    private static bool IsButton(string value)=>value.EndsWith("Button",StringComparison.OrdinalIgnoreCase);
    private static string Class(nint window){var text=new StringBuilder(128);GetClassName(window,text,text.Capacity);return text.ToString();}
    private static string Text(nint window){var text=new StringBuilder(4096);_ = SendText(window,0x000D,(nuint)text.Capacity,text,0x0002,250,out _);return text.ToString();}
    private static void Capture(nint window,string path,OwnedWindows owned)
    {
        owned.Verify(window);
        if(!GetWindowRect(window,out var rect))throw new IOException("Owned window geometry is unavailable.");int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        if(width<=0||height<=0||width>8192||height>8192)throw new IOException("Owned window capture geometry is invalid.");
        using var image=new Bitmap(width,height);using var graphics=Graphics.FromImage(image);nint dc=graphics.GetHdc();
        try{if(!PrintWindow(window,dc,2))throw new IOException("PrintWindow did not render the owned wizard.");}finally{graphics.ReleaseHdc(dc);}
        owned.Verify(window);
        image.Save(path,ImageFormat.Png);
    }
    private static string RegistrySnapshot()
    {
        var values=new SortedDictionary<string,string?>();
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view);using var key=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\R3Neer.CursorShake_is1");
            values[view+"/Exists"]=(key is not null).ToString();if(key is not null)foreach(string name in key.GetValueNames())values[view+"/"+name]=key.GetValue(name)?.ToString();
        }
        return JsonSerializer.Serialize(values);
    }
    private sealed class OwnedWindows(Process root)
    {
        public Process Root {get;}=root;
        private readonly long start=root.StartTime.ToUniversalTime().Ticks;
        private HashSet<uint> Pids()
        {
            var parents=new Dictionary<uint,uint>();using var snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot.IsInvalid)throw new IOException("Cannot enumerate owned installer processes.");
            var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>()};
            if(Process32First(snapshot,ref entry))do{parents[entry.Pid]=entry.ParentPid;}while(Process32Next(snapshot,ref entry));
            var ids=new HashSet<uint>{(uint)Root.Id};bool changed;
            do{changed=false;foreach(var pair in parents)if(ids.Contains(pair.Value))changed|=ids.Add(pair.Key);}while(changed);
            return ids;
        }
        public void Verify(nint window)
        {
            GetWindowThreadProcessId(window,out uint pid);if(pid==0||!Pids().Contains(pid))throw new IOException("Window does not belong to the launched installer tree.");
            using var owner=Process.GetProcessById((int)pid);if(owner.StartTime.ToUniversalTime().Ticks<start||owner.HasExited)throw new IOException("Installer window process identity changed.");
        }
        public IEnumerable<nint> TopLevel()
        {
            var ids=Pids();var result=new List<nint>();Exception? failure=null;
            EnumWindows((window,_)=>
            {
                try{GetWindowThreadProcessId(window,out uint pid);if(ids.Contains(pid)&&IsWindowVisible(window)){Verify(window);result.Add(window);}return true;}
                catch(Exception error){failure=error;return false;}
            },0);
            if(failure is not null)throw new IOException("Owned window enumeration could not be verified.",failure);
            return result;
        }
    }
    private sealed record ControlInfo(long Handle,int Id,string ClassName,string Text,bool Visible,bool Enabled,Rect Bounds);
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct ProcessEntry{public uint Size,Usage,Pid;public nuint Heap;public uint Module,Threads,ParentPid;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Exe;}
    private delegate bool WindowCallback(nint window,nint data);
    [DllImport("user32.dll")]private static extern bool EnumWindows(WindowCallback callback,nint data);
    [DllImport("user32.dll")]private static extern bool EnumChildWindows(nint parent,WindowCallback callback,nint data);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")]private static extern int GetDlgCtrlID(nint window);
    [DllImport("user32.dll")]private static extern nint GetAncestor(nint window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetClassNameW")]private static extern int GetClassName(nint window,StringBuilder text,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeoutW")]private static extern nint SendText(nint window,uint message,nuint w,StringBuilder text,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeoutW")]private static extern nint SendMessageTimeout(nint window,uint message,nuint w,nint l,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out Rect rect);
    [DllImport("user32.dll")]private static extern bool PrintWindow(nint window,nint dc,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="Process32FirstW")]private static extern bool Process32First(SafeFileHandle snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="Process32NextW")]private static extern bool Process32Next(SafeFileHandle snapshot,ref ProcessEntry entry);
}
