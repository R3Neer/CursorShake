using System.IO.MemoryMappedFiles;
using System.Text.Json;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;
using Xunit;

namespace CursorShake.Windows.Tests;

[Trait("Category", "NativeReadOnly")]
public sealed class RecoverySafetyTests
{
    [Theory]
    [InlineData(RecoveryCheckpoint.SessionDirectoryCreated)]
    [InlineData(RecoveryCheckpoint.BaselineCursorSaved)]
    [InlineData(RecoveryCheckpoint.BaselineManifestSaved)]
    [InlineData(RecoveryCheckpoint.LedgerCreated)]
    public void ConstructorFailureRemovesOnlyTheNewSessionAndClosesItsLedger(RecoveryCheckpoint fault)
    {
        using var fixture = new CursorFixture();
        var unrelated = fixture.WriteText("unrelated.txt", "must survive");
        bool reached = false;
        string? ledgerName = null;
        Assert.Throws<InjectedRecoveryFailure>(() => new NativeCursorSession(fixture.DirectoryPath, "unused", startGuard: false, checkpoint: phase =>
        {
            if (phase != fault) return;
            reached = true;
            if (phase == RecoveryCheckpoint.LedgerCreated)
                ledgerName = LedgerName(Assert.Single(Directory.EnumerateDirectories(Path.Combine(fixture.DirectoryPath, "recovery"))));
            throw new InjectedRecoveryFailure();
        }));
        Assert.True(reached);
        Assert.Equal("must survive", File.ReadAllText(unrelated));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.DirectoryPath, "recovery")));
        if (ledgerName is not null)
            Assert.Throws<FileNotFoundException>(() => { using var map = MemoryMappedFile.OpenExisting(ledgerName); });
    }

    [Fact]
    public void ConstructorCleanupDoesNotDeleteAnUntrackedFileAddedToItsDirectory()
    {
        using var fixture = new CursorFixture();
        string? foreign = null;
        Assert.Throws<InjectedRecoveryFailure>(() => new NativeCursorSession(fixture.DirectoryPath, "unused", startGuard: false, checkpoint: phase =>
        {
            if (phase == RecoveryCheckpoint.SessionDirectoryCreated)
            {
                var directory = Assert.Single(Directory.EnumerateDirectories(Path.Combine(fixture.DirectoryPath, "recovery")));
                foreign = Path.Combine(directory, "foreign.txt");
                File.WriteAllText(foreign, "do not delete unknown identity");
            }
            if (phase == RecoveryCheckpoint.BaselineCursorSaved) throw new InjectedRecoveryFailure();
        }));
        Assert.NotNull(foreign);
        Assert.Equal("do not delete unknown identity", File.ReadAllText(foreign!));
        Assert.Equal(new[] { foreign }, Directory.EnumerateFiles(Path.GetDirectoryName(foreign!)!, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void RestoreAndReceiptFailuresStillDisposeHandlesAndPreserveRecoverableData()
    {
        using var fixture = new CursorFixture();
        bool restoreReached = false, writeReached = false;
        var session = new NativeCursorSession(fixture.DirectoryPath, "unused", startGuard: false, checkpoint: phase =>
        {
            if (phase == RecoveryCheckpoint.BeforeRestore) { restoreReached = true; throw new InjectedRecoveryFailure(); }
            if (phase == RecoveryCheckpoint.BeforeResultWrite) { writeReached = true; throw new InjectedRecoveryFailure(); }
        });
        string directory = Assert.Single(Directory.EnumerateDirectories(Path.Combine(fixture.DirectoryPath, "recovery")));
        string name = LedgerName(directory);
        Assert.Null(Record.Exception(session.Dispose));
        Assert.True(restoreReached);
        Assert.True(writeReached);
        Assert.Throws<ObjectDisposedException>(() => session.CheckForExternalChanges());
        Assert.Throws<FileNotFoundException>(() => { using var map = MemoryMappedFile.OpenExisting(name); });
        Assert.True(File.Exists(Path.Combine(directory, "session.json")));
        var pending = Assert.Single(RecoveryBootstrap.Inspect(fixture.DirectoryPath));
        Assert.True(pending.BlocksNewSession);
        Assert.False(pending.LedgerAvailable);
        Assert.False(pending.CanAttemptRecovery);
    }

    [Fact]
    public void LiveParentOwnershipIsNeverAdoptedByBootstrap()
    {
        using var fixture = new CursorFixture();
        using var session = new NativeCursorSession(fixture.DirectoryPath, "unused", startGuard: false);
        var pending = Assert.Single(RecoveryBootstrap.Inspect(fixture.DirectoryPath));
        Assert.True(pending.ParentAlive);
        Assert.True(pending.LedgerAvailable);
        Assert.True(pending.BlocksNewSession);
        Assert.False(pending.CanAttemptRecovery);
        var result = RecoveryBootstrap.TryRecover(pending.DirectoryPath);
        Assert.False(result.AllOwnedRestored);
        Assert.Contains(result.Diagnostics, message => message.Contains("alive", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(pending.DirectoryPath, "session.json")));
    }

    [Fact]
    public async Task WorkerWaitingForRecoveryLeaseDoesNotRecreateAReceiptAlreadyCleanedByBootstrap()
    {
        using var fixture = new CursorFixture();
        using var session = new NativeCursorSession(fixture.DirectoryPath, "unused", startGuard: false);
        string directory = Assert.Single(Directory.EnumerateDirectories(Path.Combine(fixture.DirectoryPath, "recovery")));
        string ledger = LedgerName(directory);
        string manifestPath = Path.Combine(directory, "session.json");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["ParentPid"] = int.MaxValue; // No real process is stopped; every ownership ledger entry remains Untouched.
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ledger + ".Ready");
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, ledger + ".Stop");
        using var lease = new Mutex(false, ledger + ".RecoveryLock");
        Assert.True(lease.WaitOne(TimeSpan.FromSeconds(5)));
        Task<RestoreResult>? worker = null;
        try
        {
            worker = Task.Run(() => RecoveryWorker.Run(directory));
            Assert.True(ready.WaitOne(TimeSpan.FromSeconds(10)), "Worker must finish opening ownership evidence before testing the lease race.");
            var result = RecoveryBootstrap.TryRecover(directory); // Mutex is reentrant on this same test thread.
            Assert.True(result.AllOwnedRestored, string.Join("; ", result.Diagnostics));
            Assert.False(Directory.Exists(directory));
        }
        finally { lease.ReleaseMutex(); }
        Assert.NotNull(worker);
        var workerResult = await worker!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(workerResult.AllOwnedRestored, string.Join("; ", workerResult.Diagnostics));
        Assert.False(Directory.Exists(directory));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.DirectoryPath, "recovery"), "*", SearchOption.AllDirectories));
    }

    private static string LedgerName(string directory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "session.json")));
        var root = document.RootElement;
        return $"Local\\CursorShake.Recovery.{root.GetProperty("OwnerSid").GetString()}.{root.GetProperty("SessionId").GetInt32()}.{root.GetProperty("Nonce").GetString()}";
    }
    private sealed class InjectedRecoveryFailure : Exception;
}
