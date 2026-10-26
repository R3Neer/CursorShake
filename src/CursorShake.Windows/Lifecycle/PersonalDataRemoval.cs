using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Recovery;
using Microsoft.Win32;
namespace CursorShake.Windows.Lifecycle;

public static class PersonalDataRemoval
{
    public static PersonalDataRemovalPlan PrepareCurrentUser()
    {
        RequireUnelevated();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorShake");
        string state = Path.Combine(root, "state"); var locator = new LocatorStore(state, true);
        void Retire(WorkspaceLocator? expected)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\CursorShake", writable: true);
            object? raw = key?.GetValue("Workspace", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var actual = raw is null ? null : raw is string text ? JsonSerializer.Deserialize<WorkspaceLocator>(text, DurableState.Json) : throw new IOException("Workspace locator changed type.");
            if (actual != expected) throw new IOException("Workspace locator changed; personal data removal was stopped.");
            if (raw is not null) { key!.DeleteValue("Workspace", throwOnMissingValue: true); key.Flush(); }
        }
        WorkspaceLocator? ReadAuthority()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\CursorShake");
            if (key?.GetValue("Workspace", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is { } raw && raw is not string)
                throw new IOException("Workspace locator has an invalid type; personal data was preserved.");
            return locator.Read();
        }
        return Prepare(root, state, ReadAuthority, Retire, RequireUnelevated);
    }
    internal static PersonalDataRemovalPlan Prepare(string defaultRoot, string stateDirectory, Func<WorkspaceLocator?> readLocator, Action<WorkspaceLocator?> retireLocator, Action? requireUnelevated = null)
        => new(defaultRoot, stateDirectory, readLocator, retireLocator, requireUnelevated ?? (() => { }));
    private static void RequireUnelevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!GetTokenInformation(identity.AccessToken, 20, out int elevated, sizeof(int), out _)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (elevated != 0) throw new IOException("Personal data removal must run as the unelevated current user.");
    }
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token, int information, out int value, int length, out int returnedLength);
}

/// <summary>A reviewable snapshot, not a recursive delete of its workspace. Delete requires separate explicit UI consent.
/// Any later conflict throws; a partial failure keeps binary uninstallation blocked and preserves unrelated entries.</summary>
public sealed class PersonalDataRemovalPlan
{
    private readonly string state, workspace, mutexName;
    private readonly WorkspaceLocator? authority;
    private readonly Func<WorkspaceLocator?> readLocator;
    private readonly Action<WorkspaceLocator?> retireLocator;
    private readonly Action requireUnelevated;
    private readonly Dictionary<string, FileSnapshot> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly string config, themes;
    private bool deleted;
    public IReadOnlyList<string> Paths { get; }
    public int FileCount => files.Count;
    internal PersonalDataRemovalPlan(string defaultRoot, string stateDirectory, Func<WorkspaceLocator?> readLocator, Action<WorkspaceLocator?> retireLocator, Action requireUnelevated)
    {
        this.readLocator=readLocator; this.retireLocator=retireLocator; this.requireUnelevated=requireUnelevated;
        requireUnelevated(); state=WorkspacePaths.Canonical(stateDirectory);
        string sid=WindowsIdentity.GetCurrent().User?.Value ?? throw new IOException("User identity is unavailable.");
        mutexName="Global\\CursorShake.Workspace."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid+"|"+state.ToUpperInvariant())))[..24];
        using var lease=Acquire(); authority=readLocator();
        if(authority is not null && (authority.Phase!="Ready" || authority.OperationId is not null))throw new IOException("Resolve unfinished workspace operations before deleting personal data.");
        workspace=WorkspacePaths.Canonical(authority?.Root??defaultRoot); config=Path.Combine(workspace,"config.toml"); themes=Path.Combine(workspace,"themes");
        if(WorkspacePaths.Same(state,workspace)||WorkspacePaths.Inside(state,themes)||WorkspacePaths.Inside(themes,state))throw new IOException("Workspace and internal-state paths overlap; removal requires manual review.");
        ValidateState(); CaptureInternalDirectories(); CaptureFileIfPresent(config); CaptureThemes(themes);
        Paths=new[] { config, themes, state }.Where(path=>files.ContainsKey(path)||directories.ContainsKey(path)).ToArray();
    }
    public void Delete()
    {
        if(deleted)throw new InvalidOperationException("This removal plan was already committed.");
        requireUnelevated(); using var lease=Acquire(); VerifyAuthority(); ValidateState();
        var held=new List<ProtectedFile>(); var pins=new List<DirectoryProtection>();
        try
        {
            foreach(var directory in directories)
            {pins.Add(DirectoryProtection.Acquire(directory.Key));if(WorkspaceNative.DirectoryIdentity(directory.Key)!=directory.Value)throw new IOException("Directory identity changed: "+directory.Key);}
            foreach(var entry in files)
            {
                var file=ProtectedFile.Open(entry.Key,deleteAccess:true);held.Add(file);
                if(file.Identity!=entry.Value.Identity||file.Hash()!=entry.Value.Hash)throw new IOException("Personal data changed after confirmation: "+entry.Key);
            }
            ValidateState(); VerifySet(); VerifyAuthority();
            foreach(var file in held)file.DeleteSameIdentity();
        }
        finally
        {
            foreach(var file in held)file.Dispose();
            for(int i=pins.Count-1;i>=0;i--)pins[i].Dispose();
        }
        // Remove only captured empty directories, deepest first. A concurrent new entry is preserved.
        foreach(var directory in directories.OrderByDescending(d=>d.Key.Length))WorkspaceNative.RemoveEmptyDirectory(directory.Key,directory.Value);
        VerifyAuthority();retireLocator(authority);deleted=true;
    }
    private void VerifyAuthority(){if(readLocator()!=authority)throw new IOException("Workspace authority changed after confirmation; no further removal is allowed.");}
    private void ValidateState()
    {
        if(RecoveryBootstrap.Inspect(state).Count!=0)throw new IOException("Recovery receipts must be resolved before deleting personal data.");
        using var pin=ExistingDirectory(state);if(pin is null)return;
        foreach(string entry in Directory.EnumerateFileSystemEntries(state))
        {
            if(Path.GetFileName(entry)!="recovery")throw new IOException("Unknown or unfinished internal state was preserved: "+entry);
            using var recovery=DirectoryProtection.Acquire(entry);
            if(Directory.EnumerateFileSystemEntries(entry).Any())throw new IOException("Recovery state is not empty: "+entry);
        }
    }
    private void CaptureInternalDirectories()
    {
        using var pin=ExistingDirectory(state);if(pin is null)return;
        directories.Add(state,WorkspaceNative.DirectoryIdentity(state));
        foreach(string entry in Directory.EnumerateFileSystemEntries(state))directories.Add(entry,WorkspaceNative.DirectoryIdentity(entry));
    }
    private void CaptureFileIfPresent(string path)
    {
        try{using var file=ProtectedFile.Open(path);files.Add(path,new(file.Identity,file.Hash()));}
        catch(FileNotFoundException){ }
    }
    private void CaptureThemes(string path)
    {
        using var pin=ExistingDirectory(path);if(pin is null)return;
        directories.Add(path,WorkspaceNative.DirectoryIdentity(path));
        foreach(string entry in Directory.EnumerateFileSystemEntries(path))
        {
            // Attributes only choose the next operation; protected handle verification remains authoritative.
            if((File.GetAttributes(entry)&FileAttributes.Directory)!=0)CaptureThemes(entry);else CaptureFileIfPresent(entry);
        }
    }
    private void VerifySet()
    {
        var actualFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var actualDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string path)
        {
            using var pin=ExistingDirectory(path);if(pin is null)return;actualDirectories.Add(path);
            foreach(string entry in Directory.EnumerateFileSystemEntries(path))
                if((File.GetAttributes(entry)&FileAttributes.Directory)!=0)Walk(entry);else actualFiles.Add(entry);
        }
        Walk(themes);
        using(var rootPin=ExistingDirectory(workspace))
            if(rootPin is not null && Directory.EnumerateFileSystemEntries(workspace).Any(path=>string.Equals(Path.GetFileName(path),"config.toml",StringComparison.OrdinalIgnoreCase)))actualFiles.Add(config);
        using(var statePin=ExistingDirectory(state))
        {
            if(statePin is not null)
            {
                actualDirectories.Add(state);
                foreach(string entry in Directory.EnumerateFileSystemEntries(state))actualDirectories.Add(entry);
            }
        }
        if(!actualFiles.SetEquals(files.Keys)||!actualDirectories.SetEquals(directories.Keys))throw new IOException("Personal-data entries changed after confirmation; removal was stopped.");
    }
    private static DirectoryProtection? ExistingDirectory(string path)
    {try{return DirectoryProtection.Acquire(path);}catch(FileNotFoundException){return null;}}
    private IDisposable Acquire()
    {
        var mutex=new Mutex(false,mutexName);bool owned;
        try{try{owned=mutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){owned=true;}
            if(!owned)throw new IOException("Another workspace operation is running.");return new MutexLease(mutex);}
        catch{mutex.Dispose();throw;}
    }
    private sealed class MutexLease(Mutex mutex):IDisposable{public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}}
    private sealed record FileSnapshot(string Identity,string Hash);
}
