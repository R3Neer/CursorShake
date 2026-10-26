using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
namespace CursorShake.Windows.Recovery;

public sealed record RestoreResult(bool AllOwnedRestored, IReadOnlyList<string> Diagnostics);
public enum RecoveryCheckpoint
{
    SessionDirectoryCreated, BaselineCursorSaved, BaselineManifestSaved, LedgerCreated, GuardReady,
    BeforeNativeMutation, AfterNativeMutation, BeforeRestore, BeforeResultWrite
}
public sealed record PendingRecovery(string DirectoryPath, bool ParentAlive, bool LedgerAvailable, bool BlocksNewSession, string Summary)
{
    public bool CanAttemptRecovery => !ParentAlive && LedgerAvailable && BlocksNewSession;
}
internal enum OwnershipPhase { Untouched, Pending, Confirmed, Restored }
internal sealed record OwnershipState(long Generation, OwnershipPhase Phase, byte[] PreviousHash, byte[] PendingHash);
internal sealed record BaselineCursor(CursorRole Role, long SharedHandle, string PixelFile, string Fingerprint, string? SourceFile, string? SourceHash, long SourceBytes);
internal sealed record RecoveryManifest(string Nonce, int ParentPid, long ParentStartTicks, int SessionId, string OwnerSid, IReadOnlyList<BaselineCursor> Cursors)
{
    public string LedgerName => $"Local\\CursorShake.Recovery.{OwnerSid}.{SessionId}.{Nonce}";
    public string ReadyName => LedgerName + ".Ready";
    public string StopName => LedgerName + ".Stop";
}

public static class CursorFingerprint
{
    public static string Of(CursorPixels pixels) => Convert.ToHexString(Bytes(pixels));
    internal static byte[] Bytes(CursorPixels pixels)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        Span<byte> metadata = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(metadata, pixels.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(metadata[4..], pixels.Height);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(metadata[8..], pixels.HotspotX);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(metadata[12..], pixels.HotspotY);
        hash.AppendData(metadata); hash.AppendData(pixels.Bgra); hash.AppendData(pixels.AndMask);
        return hash.GetHashAndReset();
    }
    internal static byte[] Current(CursorRole role)
    {
        nint handle = CursorNative.LoadCursor(0, (nint)(uint)role);
        if (handle == 0) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Load shared cursor " + role);
        return Bytes(CursorNative.Capture(handle));
    }
}
