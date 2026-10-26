using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
namespace CursorShake.Windows.Lifecycle;

public sealed class InstallationBusyException() : IOException("CursorShake is being installed or maintained. Try again after setup finishes.");

public static class InstallationGate
{
    public static string NameFor(string root) => @"Global\CursorShake.Installation." + Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(CanonicalRoot(root).ToUpperInvariant()))).ToLowerInvariant();
    internal static string CanonicalRoot(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    public static IDisposable TryEnter(string root, TimeSpan timeout)
    {
        Mutex mutex;
        try { mutex = OpenOrCreate(NameFor(root)); }
        catch (UnauthorizedAccessException) { throw new InstallationBusyException(); }
        try
        {
            bool acquired; try { acquired = mutex.WaitOne(timeout); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InstallationBusyException();
            return new Lease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }
    /// <summary>For the separate installer helper: another thread/process must own the gate throughout maintenance.</summary>
    public static void AssertMaintenanceHeld(string root)
    {
        Mutex mutex;
        try { mutex = MutexAcl.OpenExisting(NameFor(root), MutexRights.Synchronize | MutexRights.Modify); }
        catch (WaitHandleCannotBeOpenedException e) { throw new IOException("Installer maintenance gate is not held.", e); }
        using (mutex)
        {
            bool acquired; try { acquired = mutex.WaitOne(TimeSpan.Zero); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            mutex.ReleaseMutex(); throw new IOException("Installer maintenance gate is not held by another owner.");
        }
    }
    private static Mutex OpenOrCreate(string name)
    {
        try { return MutexAcl.OpenExisting(name, MutexRights.Synchronize | MutexRights.Modify); }
        catch (WaitHandleCannotBeOpenedException) { }
        var security = new MutexSecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
        try { return MutexAcl.Create(false, name, out _, security); }
        catch (UnauthorizedAccessException) { return MutexAcl.OpenExisting(name, MutexRights.Synchronize | MutexRights.Modify); }
    }
    private sealed class Lease(Mutex mutex) : IDisposable
    {
        private readonly int owner = Environment.CurrentManagedThreadId;
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Installation mutex must be released on its acquiring thread.");
            mutex.ReleaseMutex(); mutex.Dispose(); disposed = true;
        }
    }
}
