using System.Diagnostics;
using System.Security.Principal;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Recovery;

namespace CursorShake.Windows.Tests;

public sealed class RecoverySchemeResetTests
{
    [Fact] public void EmptyStateDoesNotInvokeReset()
    {
        using var f = new CursorFixture(); int reset = 0;
        Assert.True(RecoveryBootstrap.ResetToRegisteredScheme(f.DirectoryPath, () => reset++, () => throw new Exception()).AllOwnedRestored);
        Assert.Equal(0, reset);
    }
    [Theory] [InlineData("alive")] [InlineData("user")] [InlineData("session")] [InlineData("unknown")]
    public void UnverifiedReceiptsPreventEveryNativeCall(string reason)
    {
        using var f = new CursorFixture(); string path = Receipt(f, reason); int reset = 0;
        var result = RecoveryBootstrap.ResetToRegisteredScheme(f.DirectoryPath, () => reset++, () => { });
        Assert.False(result.AllOwnedRestored); Assert.Equal(0, reset); Assert.True(Directory.Exists(path));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ResetOrValidationFailurePreservesReceipts(bool validationFailure)
    {
        using var f = new CursorFixture(); string path = Receipt(f); int reset = 0;
        var result = RecoveryBootstrap.ResetToRegisteredScheme(f.DirectoryPath,
            () => { reset++; if (!validationFailure) throw new IOException("reset failed"); },
            () => throw new IOException("validation failed"));
        Assert.False(result.AllOwnedRestored); Assert.Equal(1, reset); Assert.True(File.Exists(Path.Combine(path, "session.json")));
    }
    [Fact] public void SuccessfulExplicitResetRetiresReceiptsOnlyAfterValidation()
    {
        using var f = new CursorFixture(); string one = Receipt(f), two = Receipt(f); int reset = 0, validated = 0;
        var result = RecoveryBootstrap.ResetToRegisteredScheme(f.DirectoryPath, () => reset++, () =>
        { Assert.True(Directory.Exists(one)); Assert.True(Directory.Exists(two)); validated++; });
        Assert.True(result.AllOwnedRestored, string.Join(";", result.Diagnostics)); Assert.Equal(1, reset); Assert.Equal(1, validated);
        Assert.False(Directory.Exists(one)); Assert.False(Directory.Exists(two));
    }
    [Fact] public void UnknownFilesAreNeverRemovedByExplicitReset()
    {
        using var f = new CursorFixture(); string path = Receipt(f); File.WriteAllText(Path.Combine(path, "personal.txt"), "keep");
        int resets = 0; string original = File.ReadAllText(Path.Combine(path, "session.json"));
        var result = RecoveryBootstrap.ResetToRegisteredScheme(f.DirectoryPath, () => resets++, () => { });
        Assert.False(result.AllOwnedRestored); Assert.Equal(0, resets); Assert.Equal("keep", File.ReadAllText(Path.Combine(path, "personal.txt")));
        Assert.Equal(original, File.ReadAllText(Path.Combine(path, "session.json")));
    }
    [Fact] public void GenericCleanupPreservesManifestBeforeAnyUnknownEntryIsRetired()
    {
        using var f = new CursorFixture(); string path = Receipt(f); var manifest = BaselineStore.ReadManifest(path);
        string original = File.ReadAllText(Path.Combine(path, "session.json")); File.WriteAllText(Path.Combine(path, "foreign.txt"), "keep");
        Assert.Throws<InvalidDataException>(() => BaselineStore.Cleanup(path, manifest));
        Assert.Equal(original, File.ReadAllText(Path.Combine(path, "session.json"))); Assert.Equal("keep", File.ReadAllText(Path.Combine(path, "foreign.txt")));
    }
    [Fact] public void EntryAppearingAfterMetadataRetirementRestoresOriginalManifestWithCreateNew()
    {
        using var f = new CursorFixture(); string path = Receipt(f); var manifest = BaselineStore.ReadManifest(path);
        string original = File.ReadAllText(Path.Combine(path, "session.json"));
        Assert.Throws<IOException>(() => BaselineStore.Cleanup(path, manifest, beforeDirectoryRemoval: () => File.WriteAllText(Path.Combine(path, "late.txt"), "keep")));
        Assert.Equal(original, File.ReadAllText(Path.Combine(path, "session.json"))); Assert.Equal("keep", File.ReadAllText(Path.Combine(path, "late.txt")));
    }
    [Fact] public void NewManifestAppearingDuringRetirementIsNeverOverwritten()
    {
        using var f = new CursorFixture(); string path = Receipt(f); var manifest = BaselineStore.ReadManifest(path);
        Assert.Throws<IOException>(() => BaselineStore.Cleanup(path, manifest, beforeDirectoryRemoval: () => File.WriteAllText(Path.Combine(path, "session.json"), "new writer content")));
        Assert.Equal("new writer content", File.ReadAllText(Path.Combine(path, "session.json")));
    }
    private static string Receipt(CursorFixture f, string? reason = null)
    {
        using var process = Process.GetCurrentProcess(); string nonce = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(f.DirectoryPath, "recovery", reason == "unknown" ? "unrecognized" : nonce); Directory.CreateDirectory(directory);
        var cursors = CursorRoles.All.Select(role => new BaselineCursor(role, 1, ((uint)role) + ".pixels", new string('A', 64), null, null, 0)).ToArray();
        var manifest = new RecoveryManifest(nonce, reason == "alive" ? process.Id : int.MaxValue,
            reason == "alive" ? process.StartTime.ToUniversalTime().Ticks : 1,
            reason == "session" ? process.SessionId + 1 : process.SessionId,
            reason == "user" ? "S-1-0-0" : WindowsIdentity.GetCurrent().User!.Value, cursors);
        DurableState.Write(Path.Combine(directory, "session.json"), manifest); return directory;
    }
}
