using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
namespace CursorShake.Windows.Recovery;

public static partial class RecoveryBootstrap
{
    /// <summary>Explicit user-confirmed reset to the scheme registered in Windows. The caller must stop
    /// the engine and serialize this operation against new native sessions in its single application instance.
    /// Availability checks do not certify equality with registry files or exclude another cursor writer.</summary>
    public static RestoreResult ResetToRegisteredScheme(string stateDirectory) => ResetToRegisteredScheme(stateDirectory,
        () => { if (!SystemParametersInfo(0x0057, 0, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Reload registered Windows cursor scheme"); },
        () => { foreach (var role in CursorRoles.All) _ = CursorFingerprint.Current(role); });

    internal static RestoreResult ResetToRegisteredScheme(string stateDirectory, Action reset, Action validate)
    {
        string root = Path.Combine(Path.GetFullPath(stateDirectory), "recovery");
        var leases = new List<RecoveryLease>();
        var pinned = new List<IDisposable>();
        var receipts = new List<(string Path, RecoveryManifest Manifest, string Identity, string Hash)>();
        try
        {
            using var rootProtection = BaselineStore.ProtectExistingSession(root);
            if (rootProtection is null) return new(true, []);
            using var current = Process.GetCurrentProcess();
            string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Current user identity is unavailable.");
            foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) throw new InvalidDataException("Unknown recovery directory was preserved: " + directory);
                var manifest = BaselineStore.ReadManifest(directory);
                if (manifest.OwnerSid != sid || manifest.SessionId != current.SessionId) throw new InvalidOperationException("A recovery receipt belongs to another user or interactive session: " + directory);
                if (ParentIsAlive(manifest)) throw new InvalidOperationException("The original cursor process is still alive: " + directory);
                using var file = ProtectedFile.Open(Path.Combine(directory, "session.json"));
                receipts.Add((directory, manifest, WorkspaceNative.DirectoryIdentity(directory), file.Hash()));
            }
            if (receipts.Count == 0) return new(true, []);
            foreach (var receipt in receipts) leases.Add(new RecoveryLease(receipt.Manifest));
            foreach (var receipt in receipts)
            {
                // A guard can finish while locks are acquired. No reset is needed for its retired receipt.
                var protection = BaselineStore.ProtectExistingSession(receipt.Path);
                if (protection is null) continue;
                pinned.Add(protection);
                if (WorkspaceNative.DirectoryIdentity(receipt.Path) != receipt.Identity || !BaselineStore.ExistsWithIdentity(receipt.Path, receipt.Manifest)) throw new InvalidDataException("Recovery receipt identity changed.");
                BaselineStore.ValidateCleanupEntries(receipt.Path, receipt.Manifest);
                using var file = ProtectedFile.Open(Path.Combine(receipt.Path, "session.json"));
                if (file.Hash() != receipt.Hash || ParentIsAlive(receipt.Manifest)) throw new InvalidDataException("Recovery receipt changed or its owner is still alive.");
            }
            // The application instance owns session creation; nevertheless fail closed if a receipt appeared.
            if (Directory.EnumerateDirectories(root).Any(path => !receipts.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))) throw new InvalidOperationException("A new recovery session appeared; no reset was attempted.");
            if (pinned.Count == 0) return new(true, []);
            reset();
            validate(); // Failure preserves every receipt; no old ownership hash authorizes this new reset.
            foreach (var protection in pinned) protection.Dispose();
            pinned.Clear();
            foreach (var receipt in receipts)
            {
                if (BaselineStore.ExistsWithIdentity(receipt.Path, receipt.Manifest)) BaselineStore.ValidateCleanupEntries(receipt.Path, receipt.Manifest);
                BaselineStore.Cleanup(receipt.Path, receipt.Manifest, receipt.Identity);
            }
            return new(true, []);
        }
        catch (Exception error) { return new(false, ["The registered Windows scheme could not be reloaded or its recovery receipts retired: " + error.Message]); }
        finally
        {
            for (int i = pinned.Count - 1; i >= 0; i--) pinned[i].Dispose();
            for (int i = leases.Count - 1; i >= 0; i--) leases[i].Dispose();
        }
    }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, nint value, uint flags);
}
