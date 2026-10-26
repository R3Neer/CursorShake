using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace CursorShake.Windows.Lifecycle;

// Opt-in acceptance probe. No request means no native query, output or startup registration.
public sealed class StartupTimingProbe
{
    public sealed record Request(Guid Id, string Executable, DateTimeOffset ArmedUtc, DateTimeOffset ExpiresUtc);
    public sealed record Logon(string AuthenticationId, string Sid, int SessionId, uint LogonType, DateTimeOffset LogonUtc);
    public sealed record Measurement(Guid RequestId, string Executable, int ProcessId, Logon Session,
        DateTimeOffset ProcessStartUtc, DateTimeOffset OperationalUtc, double LogonToProcessMs,
        double InitializationMs, double TotalMs, bool UnderOneMinute);
    private readonly Request request;
    private readonly Logon logon;
    private readonly DateTimeOffset processStart;
    private readonly string root;
    private bool recorded;
    private StartupTimingProbe(Request request, Logon logon, DateTimeOffset processStart, string root)
    {this.request=request;this.logon=logon;this.processStart=processStart;this.root=root;}

    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorShake", "startup-timing");
    public static StartupTimingProbe? TryBegin(string executable)
    {
        try
        {
            string root=DataDirectory, path=Path.Combine(root,"request.json");
            var file=new FileInfo(path);
            if(!file.Exists || file.Length>4096 || file.Attributes.HasFlag(FileAttributes.ReparsePoint))return null;
            var request=JsonSerializer.Deserialize<Request>(File.ReadAllText(path));
            if(request is null || !IsArmedFor(request,executable,DateTimeOffset.UtcNow))return null;
            var logon=ReadCurrentLogon();
            // Installation, manual launch and locking/unlocking this session are not new logons.
            if(logon.LogonUtc<=request.ArmedUtc || logon.LogonType is not (2 or 11))return null;
            using var process=Process.GetCurrentProcess();
            return new(request,logon,process.StartTime.ToUniversalTime(),root);
        }
        catch(Exception error){Debug.WriteLine("Startup timing probe unavailable: "+error.Message);return null;}
    }
    internal static bool IsArmedFor(Request request,string executable,DateTimeOffset now) =>
        request.Id!=Guid.Empty && Path.IsPathFullyQualified(request.Executable) &&
        string.Equals(Path.GetFullPath(request.Executable),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase) &&
        request.ArmedUtc<=now && request.ExpiresUtc>now && request.ExpiresUtc-request.ArmedUtc<=TimeSpan.FromDays(7);

    // Called only after the host has committed a valid configuration and installed mouse input.
    public void RecordOperational()
    {
        if(recorded)return;
        recorded=true;
        string? temporary=null;
        try
        {
            using var process=Process.GetCurrentProcess();
            var measurement=CreateMeasurement(request,logon,processStart,DateTimeOffset.UtcNow,process.Id);
            temporary=Path.Combine(root,Guid.NewGuid().ToString("N")+".tmp");
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {JsonSerializer.Serialize(stream,measurement,new JsonSerializerOptions{WriteIndented=true});stream.Flush(true);}
            File.Move(temporary,Path.Combine(root,$"result-{request.Id:N}-{process.Id}.json"),false);
            temporary=null;
        }
        catch(Exception error){Debug.WriteLine("Startup timing result unavailable: "+error.Message);}
        finally{if(temporary is not null)try{File.Delete(temporary);}catch{}}
    }
    internal static Measurement CreateMeasurement(Request request,Logon logon,DateTimeOffset processStart,DateTimeOffset operational,int pid)
    {
        if(logon.LogonUtc>processStart || processStart>operational)throw new InvalidOperationException("Startup timestamps are not ordered; do not certify this sample.");
        double total=(operational-logon.LogonUtc).TotalMilliseconds;
        return new(request.Id,request.Executable,pid,logon,processStart,operational,
            (processStart-logon.LogonUtc).TotalMilliseconds,(operational-processStart).TotalMilliseconds,total,total<60000);
    }

    public static Logon ReadCurrentLogon()
    {
        using var identity=WindowsIdentity.GetCurrent();
        if(!GetTokenInformation(identity.AccessToken,10,out TokenStatistics stats,Marshal.SizeOf<TokenStatistics>(),out _))throw new Win32Exception(Marshal.GetLastWin32Error());
        int status=LsaGetLogonSessionData(ref stats.AuthenticationId,out nint buffer);
        if(status!=0)throw new Win32Exception((int)LsaNtStatusToWinError(status));
        try
        {
            if(buffer==0)throw new InvalidOperationException("Windows returned no logon data.");
            var data=Marshal.PtrToStructure<LogonData>(buffer);
            if(data.Size<Marshal.SizeOf<LogonData>() || data.Sid==0 || data.LogonTime<=0)throw new InvalidOperationException("Windows logon data is incomplete.");
            using var process=Process.GetCurrentProcess();
            string sid=new SecurityIdentifier(data.Sid).Value;
            if(sid!=identity.User?.Value || data.Session!=(uint)process.SessionId)throw new InvalidOperationException("Logon identity/session does not match this process.");
            return new($"{stats.AuthenticationId.High:x8}{stats.AuthenticationId.Low:x8}",sid,process.SessionId,data.LogonType,DateTimeOffset.FromFileTime(data.LogonTime));
        }
        finally{if(buffer!=0)LsaFreeReturnBuffer(buffer);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid {public uint Low; public int High;}
    [StructLayout(LayoutKind.Sequential)] private struct TokenStatistics
    {public Luid TokenId,AuthenticationId;public long Expiration;public int TokenType,Impersonation;public uint DynamicCharged,DynamicAvailable,GroupCount,PrivilegeCount;public Luid ModifiedId;}
    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString {public ushort Length,MaximumLength;public nint Buffer;}
    // Prefix through LogonTime of SECURITY_LOGON_SESSION_DATA, with native x64 alignment.
    [StructLayout(LayoutKind.Sequential)] private struct LogonData
    {public uint Size;public Luid LogonId;public UnicodeString User,Domain,Package;public uint LogonType,Session;public nint Sid;public long LogonTime;}
    [DllImport("advapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token,int information,out TokenStatistics stats,int length,out int returned);
    [DllImport("secur32.dll")]private static extern int LsaGetLogonSessionData(ref Luid id,out nint data);
    [DllImport("secur32.dll")]private static extern uint LsaNtStatusToWinError(int status);
    [DllImport("secur32.dll")]private static extern int LsaFreeReturnBuffer(nint buffer);
}
