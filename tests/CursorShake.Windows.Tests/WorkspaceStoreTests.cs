using CursorShake.Windows.Configuration;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class WorkspaceStoreTests
{
    [Fact]
    public void MissingConfigurationIsGeneratedButPresentInvalidTextIsPreserved()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        var generated = store.EnsureConfiguration();
        Assert.Equal(ConfigurationDefaults.Text, generated);
        Assert.Equal(generated, File.ReadAllText(store.ConfigPath));
        File.WriteAllText(store.ConfigPath, "version = [");
        Assert.Equal("version = [", store.EnsureConfiguration());
        Assert.Equal("version = [", File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void ProtectedUpdateChecksRevisionAndPreservesUnrelatedText()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        store.EnsureConfiguration();
        const string original = "version = 1\n# Keep this note\n[appearance]\ntheme = \"windows\"\n";
        File.WriteAllText(store.ConfigPath, original);
        var snapshot = store.Read();
        var updated = store.ProtectedUpdate(text => text.Replace("\"windows\"", "\"nordzy-white\""), snapshot.Revision);
        Assert.Contains("# Keep this note", updated.Text);
        Assert.Contains("nordzy-white", updated.Text);
        Assert.NotEqual(snapshot.Revision, updated.Revision);
        Assert.Equal(updated.Text, File.ReadAllText(store.ConfigPath));
        Assert.Throws<ConfigurationException>(() => store.ProtectedUpdate(text => text, snapshot.Revision));
        Assert.Equal(updated.Text, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void InvalidProtectedUpdateDoesNotOverwriteTheCurrentFile()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        store.EnsureConfiguration();
        var before = store.Read();
        Assert.Throws<ConfigurationException>(() => store.ProtectedUpdate(_ => "version = [", before.Revision));
        Assert.Equal(before.Text, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void WriterHoldingTheFileProducesBusyWithoutClosingOrReplacingIt()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        store.EnsureConfiguration();
        var before = store.Read();
        using (var editor = new FileStream(store.ConfigPath, FileMode.Open, FileAccess.Write, FileShare.Read))
            Assert.Throws<ConfigurationException>(() => store.ProtectedUpdate(text => text, before.Revision));
        Assert.Equal(before.Text, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void ExistingDestinationTomlPreventsMoveWithoutOverwritingEitherFile()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        store.EnsureConfiguration();
        var source = store.RootPath;
        var destination = Path.Combine(fixture.DirectoryPath, "destination");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "config.toml"), "destination must survive");
        Assert.Throws<ConfigurationException>(() => store.MoveTo(destination));
        Assert.Equal(source, store.RootPath);
        Assert.Equal(ConfigurationDefaults.Text, File.ReadAllText(store.ConfigPath));
        Assert.Equal("destination must survive", File.ReadAllText(Path.Combine(destination, "config.toml")));
    }

    [Fact]
    public void SuccessfulMoveChangesAuthorityBeforeCleaningOnlyManagedOriginals()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        store.EnsureConfiguration();
        var source = store.RootPath;
        var oldConfig = store.ConfigPath;
        var oldTheme = Path.Combine(store.ThemesPath, "custom", "pointer.cur");
        Directory.CreateDirectory(Path.GetDirectoryName(oldTheme)!);
        File.WriteAllBytes(oldTheme, TestCursorFiles.Cur());
        var unrelated = Path.Combine(source, "unrelated.txt");
        File.WriteAllText(unrelated, "leave me here");
        var destination = Path.Combine(fixture.DirectoryPath, "destination");
        bool prepared = false;
        store.MoveTo(destination, candidate =>
        {
            Assert.True(File.Exists(Path.Combine(candidate, "config.toml")));
            Assert.True(File.Exists(oldConfig));
            prepared = true;
        });
        Assert.True(prepared);
        Assert.Equal(Path.GetFullPath(destination), store.RootPath);
        Assert.True(File.Exists(oldConfig));
        Assert.True(File.Exists(oldTheme));
        Assert.Equal(TestCursorFiles.Cur(), File.ReadAllBytes(Path.Combine(destination, "themes", "custom", "pointer.cur")));
        Assert.True(store.CompletePendingMove());
        Assert.False(File.Exists(oldConfig));
        Assert.False(File.Exists(oldTheme));
        Assert.Equal("leave me here", File.ReadAllText(unrelated));
        Assert.Equal(Path.GetFullPath(destination), store.RootPath);
    }

    [Fact]
    public void FailureBeforeCommitLeavesSourceAuthoritativeAndRecoverable()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        store.EnsureConfiguration();
        var source = store.RootPath;
        var original = store.Read();
        var destination = Path.Combine(fixture.DirectoryPath, "destination");
        bool reachedPreparation = false;
        Assert.ThrowsAny<Exception>(() => store.MoveTo(destination, _ =>
        {
            reachedPreparation = true;
            throw new InvalidOperationException("Injected preparation failure");
        }));
        Assert.True(reachedPreparation);
        Assert.Equal(source, store.RootPath);
        Assert.Equal(original.Text, File.ReadAllText(store.ConfigPath));
        store.RecoverPendingOperations();
        Assert.Equal(source, store.RootPath);
        Assert.Equal(original.Text, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void RestartAfterCommitUsesDestinationWithoutPrematureSourceRemoval()
    {
        using var fixture = new CursorFixture();
        var source = Path.Combine(fixture.DirectoryPath, "source");
        var state = Path.Combine(fixture.DirectoryPath, "state");
        var store = new WorkspaceStore(source, state, persistLocator: false);
        store.EnsureConfiguration();
        var oldConfig = store.ConfigPath;
        var destination = Path.Combine(fixture.DirectoryPath, "destination");
        store.MoveTo(destination);
        var restarted = new WorkspaceStore(source, state, persistLocator: false);
        restarted.RecoverPendingOperations();
        Assert.Equal(Path.GetFullPath(destination), restarted.RootPath);
        Assert.True(File.Exists(oldConfig));
        Assert.Equal(ConfigurationDefaults.Text, File.ReadAllText(restarted.ConfigPath));
        Assert.True(restarted.CompletePendingMove());
        Assert.False(File.Exists(oldConfig));
    }

    [Fact]
    public void EditedOriginalAfterCommitIsKeptRatherThanDeletedByStaleManifest()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        store.EnsureConfiguration();
        var oldConfig = store.ConfigPath;
        var destination = Path.Combine(fixture.DirectoryPath, "destination");
        store.MoveTo(destination);
        const string edited = "version=1\n# edited after commit";
        File.WriteAllText(oldConfig, edited);
        Assert.False(store.CompletePendingMove());
        Assert.Contains(store.RecoveryNotices, notice => notice.Code == "CleanupPending");
        Assert.Equal(edited, File.ReadAllText(oldConfig));
        Assert.Equal(Path.GetFullPath(destination), store.RootPath);
        Assert.Equal(ConfigurationDefaults.Text, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void NestedDestinationIsRejectedBeforeMoving()
    {
        using var fixture = new CursorFixture();
        var store = NewStore(fixture);
        store.EnsureConfiguration();
        var source = store.RootPath;
        Assert.Throws<ConfigurationException>(() => store.MoveTo(Path.Combine(source, "nested")));
        Assert.Equal(source, store.RootPath);
        Assert.True(File.Exists(store.ConfigPath));
    }

    [Theory]
    [InlineData(WorkspaceCheckpoint.WriteJournalDurable, false)]
    [InlineData(WorkspaceCheckpoint.WriteFlushed, true)]
    public void RecoveringACompleteBeforeOrAfterWriteNeverChoosesByTimestamp(WorkspaceCheckpoint checkpoint, bool candidateWasFlushed)
    {
        using var fixture = new CursorFixture();
        var source = Path.Combine(fixture.DirectoryPath, "source");
        var state = Path.Combine(fixture.DirectoryPath, "state");
        bool injected = false;
        var store = new WorkspaceStore(source, state, persistLocator: false, checkpoint: step =>
        {
            if (step == checkpoint) { injected = true; throw new InvalidOperationException("Injected write failure"); }
        });
        store.EnsureConfiguration();
        var before = store.Read();
        var candidate = before.Text + "\n# retained candidate marker\n";
        Assert.Throws<InvalidOperationException>(() => store.ProtectedUpdate(_ => candidate, before.Revision));
        Assert.True(injected);
        var restarted = new WorkspaceStore(source, state, persistLocator: false);
        Assert.Equal(candidateWasFlushed ? candidate : before.Text, File.ReadAllText(restarted.ConfigPath));
        Assert.DoesNotContain(restarted.RecoveryNotices, notice => notice.Code == "RecoveryRequired");
        Assert.Empty(Directory.EnumerateFiles(state, "*.write.json"));
    }

    [Fact]
    public void PartialWriteRecoveryPreservesActualTextAndBothVersions()
    {
        using var fixture = new CursorFixture();
        var source = Path.Combine(fixture.DirectoryPath, "source");
        var state = Path.Combine(fixture.DirectoryPath, "state");
        bool injected = false;
        var store = new WorkspaceStore(source, state, persistLocator: false, checkpoint: step =>
        {
            if (step == WorkspaceCheckpoint.WriteStarted) { injected = true; throw new InvalidOperationException("Injected partial write"); }
        });
        store.EnsureConfiguration();
        var before = store.Read();
        var candidate = before.Text + "\n# candidate\n";
        Assert.Throws<InvalidOperationException>(() => store.ProtectedUpdate(_ => candidate, before.Revision));
        Assert.True(injected);
        var actual = File.ReadAllText(store.ConfigPath);
        var restarted = new WorkspaceStore(source, state, persistLocator: false);
        Assert.Equal(actual, File.ReadAllText(restarted.ConfigPath));
        var notice = Assert.Single(restarted.RecoveryNotices, n => n.Code == "RecoveryRequired");
        Assert.NotNull(notice.RelatedPaths);
        Assert.Equal(2, notice.RelatedPaths!.Count);
        Assert.Contains(notice.RelatedPaths, p => File.ReadAllText(p) == before.Text);
        Assert.Contains(notice.RelatedPaths, p => File.ReadAllText(p) == candidate);
    }

    [Fact]
    public void FailureImmediatelyAfterDurableRootCommitRecoversDestinationAuthority()
    {
        using var fixture = new CursorFixture();
        var source = Path.Combine(fixture.DirectoryPath, "source");
        var state = Path.Combine(fixture.DirectoryPath, "state");
        var destination = Path.Combine(fixture.DirectoryPath, "destination");
        bool injected = false;
        var store = new WorkspaceStore(source, state, persistLocator: false, checkpoint: step =>
        {
            if (step == WorkspaceCheckpoint.RootCommitted) { injected = true; throw new InvalidOperationException("Injected commit failure"); }
        });
        store.EnsureConfiguration();
        var oldConfig = store.ConfigPath;
        int notifications = 0;
        store.RootChanged += () => notifications++;
        Assert.Throws<InvalidOperationException>(() => store.MoveTo(destination));
        Assert.True(injected);
        Assert.Equal(1, notifications);
        var restarted = new WorkspaceStore(source, state, persistLocator: false);
        Assert.Equal(Path.GetFullPath(destination), restarted.RootPath);
        Assert.True(File.Exists(oldConfig));
        Assert.Equal(ConfigurationDefaults.Text, File.ReadAllText(restarted.ConfigPath));
        Assert.True(restarted.CompletePendingMove());
        Assert.False(File.Exists(oldConfig));
    }

    [Fact]
    public void AnOlderStoreRefreshesDurableAuthorityBeforeRegeneratingMissingConfiguration()
    {
        using var fixture = new CursorFixture();
        var source = Path.Combine(fixture.DirectoryPath, "source");
        var state = Path.Combine(fixture.DirectoryPath, "state");
        var destination = Path.Combine(fixture.DirectoryPath, "destination");
        var oldStore = new WorkspaceStore(source, state, persistLocator: false);
        oldStore.EnsureConfiguration();
        var oldConfig = oldStore.ConfigPath;
        var movingStore = new WorkspaceStore(source, state, persistLocator: false);
        movingStore.MoveTo(destination);
        Assert.True(movingStore.CompletePendingMove());
        Assert.False(File.Exists(oldConfig));
        Assert.Equal(ConfigurationDefaults.Text, oldStore.EnsureConfiguration());
        Assert.Equal(Path.GetFullPath(destination), oldStore.RootPath);
        Assert.False(File.Exists(oldConfig));
        Assert.Equal(ConfigurationDefaults.Text, oldStore.Read().Text);
    }

    [Fact]
    public void FailureAfterLocatorWriteBeforeFlushDoesNotRollAuthorityBackOrDeleteDestination()
    {
        using var fixture = new CursorFixture();
        var source = Path.Combine(fixture.DirectoryPath, "source");
        var state = Path.Combine(fixture.DirectoryPath, "state");
        var destination = Path.Combine(fixture.DirectoryPath, "destination");
        bool inject = false, reached = false;
        var store = new WorkspaceStore(source, state, persistLocator: false, checkpoint: step =>
        {
            if (inject && step == WorkspaceCheckpoint.LocatorWrittenBeforeFlush)
            {
                reached = true;
                throw new IOException("Injected uncertain locator flush");
            }
        });
        store.EnsureConfiguration();
        var oldConfig = store.ConfigPath;
        int notifications = 0;
        store.RootChanged += () => notifications++;
        inject = true;
        Assert.ThrowsAny<Exception>(() => store.MoveTo(destination));
        Assert.True(reached);
        Assert.Equal(Path.GetFullPath(destination), store.RootPath);
        Assert.Equal(1, notifications);
        Assert.True(File.Exists(oldConfig));
        Assert.Equal(ConfigurationDefaults.Text, File.ReadAllText(Path.Combine(destination, "config.toml")));
        var restarted = new WorkspaceStore(source, state, persistLocator: false);
        Assert.Equal(Path.GetFullPath(destination), restarted.RootPath);
        Assert.True(restarted.CompletePendingMove());
        Assert.False(File.Exists(oldConfig));
    }

    private static WorkspaceStore NewStore(CursorFixture fixture) => new(Path.Combine(fixture.DirectoryPath, "source"), Path.Combine(fixture.DirectoryPath, "state"), persistLocator: false);
}
