using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
namespace CursorShake.Windows.Recovery;

internal static class RecoveryRestorer
{
    public static RestoreResult Restore(string directory, RecoveryManifest manifest, OwnershipLedger ledger)
    {
        var errors = new List<string>();
        if (ledger.ExternalChange)
        {
            try
            {
                // Already restored is a read-only success, not permission to take ownership back.
                return BaselineAlreadyPresent(manifest) ? new(true, [])
                    : new(false, ["An external cursor change was observed. Recovery did not overwrite it."]);
            }
            catch(Exception error) { return new(false, ["The original cursors could not be verified without restoration: "+error.Message]); }
        }
        try
        {
            // Check all roles before attempting any compensation; a scheme switch can affect untouched roles too.
            for (int i = 0; i < manifest.Cursors.Count; i++)
            {
                var role = manifest.Cursors[i]; var owned = ledger.Read(i); byte[] current = CursorFingerprint.Current(role.Role);
                bool expected = owned.Phase is OwnershipPhase.Untouched or OwnershipPhase.Restored ? Convert.ToHexString(current) == role.Fingerprint : current.AsSpan().SequenceEqual(owned.PreviousHash) || current.AsSpan().SequenceEqual(owned.PendingHash);
                if (!expected || CursorNative.LoadCursor(0, (nint)(uint)role.Role) != (nint)role.SharedHandle)
                { ledger.SetExternalChange(); return new(false, ["External or unverified cursor detected for " + role.Role + "; recovery abstained."]); }
            }
        }
        catch (Exception e) { return new(false, ["Cannot verify the current desktop cursors: " + e.Message]); }

        for (int i = 0; i < manifest.Cursors.Count; i++)
        {
            var role = manifest.Cursors[i]; var owned = ledger.Read(i);
            if (owned.Phase is OwnershipPhase.Untouched or OwnershipPhase.Restored) continue;
            try
            {
                byte[] current = CursorFingerprint.Current(role.Role);
                if (!current.AsSpan().SequenceEqual(owned.PreviousHash) && !current.AsSpan().SequenceEqual(owned.PendingHash))
                { ledger.SetExternalChange(); errors.Add("Cursor changed before restoration: " + role.Role); break; }
                var pixels = BaselineStore.ReadPixels(WorkspacePaths.Child(directory, role.PixelFile));
                if (CursorFingerprint.Of(pixels) != role.Fingerprint) throw new InvalidDataException("Baseline pixels were changed: " + role.Role);
                using var source = LoadBaseline(directory, role, pixels);
                byte[] baselineHash = Convert.FromHexString(role.Fingerprint);
                ledger.Write(i, OwnershipPhase.Pending, current, baselineHash);
                if (!CursorNative.SetSystemCursor(source.Transfer(), (uint)role.Role)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Restore system cursor " + role.Role);
                if (!CursorFingerprint.Current(role.Role).AsSpan().SequenceEqual(baselineHash)) throw new InvalidOperationException("Restored pixels/hotspot differ: " + role.Role);
                ledger.Write(i, OwnershipPhase.Restored, baselineHash, baselineHash);
            }
            catch (Exception e) { errors.Add(role.Role + ": " + e.Message); }
        }
        return new(errors.Count == 0 && !ledger.ExternalChange, errors);
    }
    internal static bool BaselineAlreadyPresent(RecoveryManifest manifest)
    {
        foreach(var role in manifest.Cursors)
            if(CursorNative.LoadCursor(0,(nint)(uint)role.Role)!=(nint)role.SharedHandle||Convert.ToHexString(CursorFingerprint.Current(role.Role))!=role.Fingerprint)return false;
        return true;
    }
    private static SafeCursorHandle LoadBaseline(string directory, BaselineCursor role, CursorPixels pixels)
    {
        if (role.SourceFile is null) return CursorNative.Create(pixels);
        string path = WorkspacePaths.Child(directory, role.SourceFile);
        using var protectedSource = ProtectedFile.Open(path);
        if (protectedSource.Hash() != role.SourceHash) throw new InvalidDataException("Original cursor bytes were changed: " + path);
        var restored = CursorNative.LoadFile(path, pixels.Width, pixels.Height);
        if (CursorFingerprint.Of(CursorNative.Capture(restored.DangerousGetHandle())) == role.Fingerprint) return restored;
        restored.Dispose(); throw new InvalidDataException("Original cursor file no longer matches the baseline.");
    }
}
