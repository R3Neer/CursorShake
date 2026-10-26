using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Recovery;
namespace CursorShake.Windows.Lifecycle;

/// <summary>Durable admission records survive inactive sessions. Scope/index absence is never proof
/// that an installed application has no outstanding recovery receipts. No offline profile is modified.</summary>
public static class InstallationRecoveryIndex
{
    public static void Provision(string root) => Provision(root, IndexContext.Current());
    public static void RegisterCurrentUser(string root, string stateDirectory) => RegisterCurrentUser(root, stateDirectory, IndexContext.Current());
    public static void AssertClear(string root) => AssertClear(root, IndexContext.Current());
    public static void AssertBeforeUpdate(string root) => AssertBeforeUpdate(root, IndexContext.Current());
    internal static void AssertBeforeUpdate(string root, IndexContext context)
    {
        context.RequireGate(root);
        if(ReadScope(root) is not null)AssertClear(root,context);
        else VerifyPortableAbsence(root,context);
    }
    private static void VerifyPortableAbsence(string root, IndexContext context)
    {
        foreach(string candidate in new[]{"user","all-users"})
        {
            string hashDirectory=IndexDirectories(root,candidate,context).ElementAt(1);
            using var existing=ExistingDirectory(hashDirectory);
            if(existing is not null)throw new IOException("Installation scope is missing although a recovery index exists; native admission was refused.");
        }
        if(context.RegisteredInstallation?.Invoke(root)==true)throw new IOException("Installation scope is missing for a registered installation; native admission was refused.");
    }

    internal static void Provision(string root, IndexContext context)
    {
        context.RequireGate(root);
        string scope = ReadScope(root) ?? throw new IOException("Installation scope is missing; recovery-index provisioning was refused.");
        if(scope=="all-users"&&!context.Elevated)throw new IOException("The all-users recovery index must be provisioned by an elevated installer.");
        foreach(string directory in IndexDirectories(root,scope,context))
            CreateAndVerify(directory,RootSecurity(scope,context,Path.GetFileName(directory)=="Users"),OwnerAllowed(scope,context));
    }
    internal static void RegisterCurrentUser(string root,string stateDirectory,IndexContext context)
    {
        string? scope=ReadScope(root);
        if(scope is null)
        {
            VerifyPortableAbsence(root,context);
            return;
        }
        string users=VerifyIndex(root,scope,context);using var pin=Pin(users);
        var record=new UserRecord(1,context.Sid.Value,WorkspacePaths.Canonical(stateDirectory));
        string path=Path.Combine(users,context.Sid.Value+".json");
        try
        {
            using var existing=ProtectedFile.Open(Extended(path));VerifyFileSecurity(path,context.Sid);
            if(ReadRecord(existing)!=record)throw new IOException("An existing recovery-index state path cannot be changed silently: "+path);
            return;
        }
        catch(FileNotFoundException){ }
        string temporary=Path.Combine(users,context.Sid.Value+"."+Guid.NewGuid().ToString("N")+".tmp");
        string? identity=null,hash=null;
        try
        {
            CreateRecordFile(temporary,context.Sid,JsonSerializer.SerializeToUtf8Bytes(record,DurableState.Json));
            using(var file=ProtectedFile.Open(Extended(temporary))){identity=file.Identity;hash=file.Hash();}
            if(!MoveFile(Extended(temporary),Extended(path),8))throw new IOException("Cannot publish recovery admission without replacing an existing record.",new Win32Exception(Marshal.GetLastWin32Error()));
        }
        finally
        {
            if(identity is not null)
                try{using var file=ProtectedFile.Open(Extended(temporary),deleteAccess:true);if(file.Identity==identity&&file.Hash()==hash)file.DeleteSameIdentity();}
                catch(FileNotFoundException){ }
        }
    }
    internal static void AssertClear(string root,IndexContext context)
    {
        context.RequireGate(root);
        string scope=ReadScope(root)??throw new IOException("Installation scope is absent; no durable recovery index can be verified. Preserve the installation and resolve it manually.");
        if(scope=="all-users"&&!context.Elevated)throw new IOException("All-users recovery verification requires the elevated uninstaller.");
        string users=VerifyIndex(root,scope,context);using var pin=Pin(users);
        foreach(string path in Directory.EnumerateFileSystemEntries(users).Order(StringComparer.OrdinalIgnoreCase))
        {
            string text=Path.GetFileNameWithoutExtension(path);SecurityIdentifier sid;
            try{sid=new SecurityIdentifier(text);}catch(ArgumentException e){throw new IOException("Unknown recovery-index entry was preserved: "+path,e);}
            if(sid.Value!=text||Path.GetFileName(path)!=text+".json")throw new IOException("Recovery-index filename is not canonical: "+path);
            using var file=ProtectedFile.Open(Extended(path));VerifyFileSecurity(path,sid);var record=ReadRecord(file);
            // A missing/unmounted volume is not proof that the registered user's state is absent.
            using var volume=Pin(Path.GetPathRoot(record.StateDirectory)??throw new IOException("State path has no accessible volume root."));
            using var state=ExistingDirectory(record.StateDirectory);if(state is null)continue;
            if(context.Inspect(record.StateDirectory).Count!=0)throw new IOException("Registered user "+record.Sid+" has pending or unverifiable recovery state at "+record.StateDirectory+". Resolve it before uninstalling.");
        }
    }
    private static UserRecord ReadRecord(ProtectedFile file)
    {
        var record=JsonSerializer.Deserialize<UserRecord>(file.ReadBytes(16384),DurableState.Json)??throw new IOException("Recovery-index record is empty.");
        if(record.Version!=1||record.Sid+".json"!=Path.GetFileName(file.Path)||!Path.IsPathFullyQualified(record.StateDirectory)||WorkspacePaths.Canonical(record.StateDirectory)!=record.StateDirectory)
            throw new IOException("Recovery-index record identity or state path is invalid: "+file.Path);
        return record;
    }
    private static string VerifyIndex(string root,string scope,IndexContext context)
    {
        string? last=null;
        foreach(string directory in IndexDirectories(root,scope,context))
        {using var pin=Pin(directory);VerifySecurity(directory,RootSecurity(scope,context,Path.GetFileName(directory)=="Users"),OwnerAllowed(scope,context));last=directory;}
        return last!;
    }
    internal static string UsersDirectory(string root,string scope,IndexContext context)=>IndexDirectories(root,scope,context).Last();
    private static IEnumerable<string> IndexDirectories(string root,string scope,IndexContext context)
    {
        string hash=InstallationGate.NameFor(root)[@"Global\CursorShake.Installation.".Length..];
        string current=Path.Combine(scope=="all-users"?context.CommonData:context.LocalData,"CursorShake.Installations");yield return current;
        current=Path.Combine(current,hash);yield return current;
        yield return Path.Combine(current,"Users");
    }
    private static string? ReadScope(string root)
    {
        string path=Path.Combine(InstallationGate.CanonicalRoot(root),"maintenance-scope.txt");
        try{using var file=ProtectedFile.Open(Extended(path));string value=ProtectedFile.Decode(file.ReadBytes(64)).TrimEnd('\r','\n');
            if(value is not "user" and not "all-users")throw new IOException("Installation scope marker is invalid: "+path);return value;}
        catch(FileNotFoundException){return null;}
    }
    private static string Extended(string path)
    {
        string full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\?\",StringComparison.Ordinal))return full;
        return full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full[2..]:@"\\?\"+full;
    }
    private static DirectoryProtection Pin(string path)=>DirectoryProtection.Acquire(Extended(path));
    private static DirectoryProtection? ExistingDirectory(string path)
    {try{return Pin(path);}catch(FileNotFoundException){return null;}}
    private static readonly SecurityIdentifier Admins=new(WellKnownSidType.BuiltinAdministratorsSid,null),SystemSid=new(WellKnownSidType.LocalSystemSid,null),UsersSid=new(WellKnownSidType.BuiltinUsersSid,null);
    private static bool TrustedOwner(SecurityIdentifier owner,SecurityIdentifier sid)=>owner.Equals(sid)||owner.Equals(Admins)||owner.Equals(SystemSid);
    private static Func<SecurityIdentifier,bool> OwnerAllowed(string scope,IndexContext context)=>scope=="all-users"?owner=>owner.Equals(Admins)||owner.Equals(SystemSid):owner=>TrustedOwner(owner,context.Sid);
    private static DirectorySecurity RootSecurity(string scope,IndexContext context,bool usersLeaf)
    {
        var security=new DirectorySecurity();security.SetOwner(scope=="all-users"?Admins:context.Sid);security.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{Admins,SystemSid,scope=="all-users"?null:context.Sid}.Where(s=>s is not null).Distinct())
            security.AddAccessRule(new FileSystemAccessRule(sid!,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        if(scope=="all-users")security.AddAccessRule(new FileSystemAccessRule(UsersSid,FileSystemRights.ReadAndExecute|(usersLeaf?FileSystemRights.CreateFiles:0),InheritanceFlags.None,PropagationFlags.None,AccessControlType.Allow));
        return security;
    }
    private static FileSecurity FileSecurityFor(SecurityIdentifier sid)
    {
        var security=new FileSecurity();security.SetOwner(sid);security.SetAccessRuleProtection(true,false);
        foreach(var identity in new[]{sid,Admins,SystemSid}.Distinct())security.AddAccessRule(new FileSystemAccessRule(identity,FileSystemRights.FullControl,AccessControlType.Allow));
        return security;
    }
    private static void VerifyFileSecurity(string path,SecurityIdentifier sid)
    {
        var actual=new FileInfo(path).GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access);
        VerifyAcl(path,actual,FileSecurityFor(sid),owner=>TrustedOwner(owner,sid));
    }
    private static void VerifySecurity(string path,DirectorySecurity expected,Func<SecurityIdentifier,bool> ownerAllowed)
    {
        using var pin=Pin(path);
        VerifyAcl(path,new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access),expected,ownerAllowed);
    }
    private static void VerifyAcl(string path,FileSystemSecurity actual,FileSystemSecurity expected,Func<SecurityIdentifier,bool> ownerAllowed)
    {
        if(!actual.AreAccessRulesProtected||actual.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner||!ownerAllowed(owner))throw new IOException("Recovery-index owner or inheritance cannot be trusted: "+path);
        static string[] Rules(FileSystemSecurity security)=>security.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r=>$"{r.IdentityReference.Value}|{(int)r.FileSystemRights}|{(int)r.InheritanceFlags}|{(int)r.PropagationFlags}|{(int)r.AccessControlType}|{r.IsInherited}").Order(StringComparer.Ordinal).ToArray();
        if(!Rules(actual).SequenceEqual(Rules(expected)))throw new IOException("Recovery-index permissions differ from their protected contract: "+path);
    }
    private static void CreateRecordFile(string path,SecurityIdentifier sid,byte[] bytes)
    {
        byte[] descriptor=FileSecurityFor(sid).GetSecurityDescriptorBinaryForm();var pinned=GCHandle.Alloc(descriptor,GCHandleType.Pinned);
        try
        {
            var attributes=new SecurityAttributes{Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=pinned.AddrOfPinnedObject()};
            using var handle=CreateFile(Extended(path),0xC0000000,FileShare.Read,ref attributes,1,0x00200000,0);
            if(handle.IsInvalid)throw new IOException("Cannot exclusively create recovery admission: "+path,new Win32Exception(Marshal.GetLastWin32Error()));
            using var stream=new FileStream(handle,FileAccess.ReadWrite);stream.Write(bytes);stream.Flush(true);
        }
        finally{pinned.Free();}
    }
    private static void CreateAndVerify(string path,DirectorySecurity security,Func<SecurityIdentifier,bool> ownerAllowed)
    {
        using var parents=Pin(Path.GetDirectoryName(path)!);
        byte[] descriptor=security.GetSecurityDescriptorBinaryForm();var pinned=GCHandle.Alloc(descriptor,GCHandleType.Pinned);
        try
        {
            var attributes=new SecurityAttributes{Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=pinned.AddrOfPinnedObject()};
            if(!CreateDirectory(Extended(path),ref attributes))
            {int error=Marshal.GetLastWin32Error();if(error!=183)throw new IOException("Cannot create protected recovery index: "+path,new Win32Exception(error));}
        }
        finally{pinned.Free();}
        VerifySecurity(path,security,ownerAllowed);
    }
    [StructLayout(LayoutKind.Sequential)]private struct SecurityAttributes{public int Length;public nint Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
    [DllImport("kernel32.dll",EntryPoint="CreateDirectoryW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreateDirectory(string path,ref SecurityAttributes security);
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path,uint access,FileShare share,ref SecurityAttributes security,uint creation,uint flags,nint template);
    [DllImport("kernel32.dll",EntryPoint="MoveFileExW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool MoveFile(string source,string destination,uint flags);
    private static bool IsRegisteredRoot(string root)
    {
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            using var registry=RegistryKey.OpenBaseKey(hive,view);
            using var key=registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\R3Neer.CursorShake_is1");
            if(key is null)continue;
            object? location=key.GetValue("InstallLocation",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            if(location is not string text||!Path.IsPathFullyQualified(text))throw new IOException("A CursorShake uninstall registration has an invalid installation location.");
            if(WorkspacePaths.Same(text,root))return true;
        }
        return false;
    }
    private sealed record UserRecord(int Version,string Sid,string StateDirectory);
    internal sealed record IndexContext(string LocalData,string CommonData,SecurityIdentifier Sid,bool Elevated,Action<string> RequireGate,Func<string,IReadOnlyList<PendingRecovery>> Inspect,Func<string,bool>? RegisteredInstallation=null)
    {
        public static IndexContext Current()
        {
            using var identity=WindowsIdentity.GetCurrent();var sid=identity.User??throw new IOException("Current SID is unavailable.");
            return new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),sid,new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),InstallationGate.AssertMaintenanceHeld,RecoveryBootstrap.Inspect,IsRegisteredRoot);
        }
    }
}
