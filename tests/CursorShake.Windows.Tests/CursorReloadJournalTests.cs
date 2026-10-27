using System.Text.Json;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Lifecycle;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class CursorReloadJournalTests
{
    [Fact]
    public void CompletionPreservesTheFirstNativeDiagnosticAndObservationTime()
    {
        using var directory = new CursorFixture();
        var journal = new CursorReloadJournal(Path.Combine(directory.DirectoryPath, "state"));
        journal.Detected("Arrow fingerprint diverged before publish; registry unchanged.");
        var detected = Read(directory);
        Assert.Equal("Detected", detected.Outcome); Assert.Null(detected.Detail);
        journal.Completed("Recovered", "All17 roles were verified and republished.");
        var completed = Read(directory);
        Assert.Equal(detected.Diagnostic, completed.Diagnostic);
        Assert.Equal(detected.ObservedUtc, completed.ObservedUtc);
        Assert.Equal("Recovered", completed.Outcome);
        Assert.Equal("All17 roles were verified and republished.", completed.Detail);
        Assert.True(completed.UpdatedUtc >= completed.ObservedUtc);
        Assert.Equal(new[] { CursorReloadJournal.FileName }, Directory.GetFileSystemEntries(directory.DirectoryPath).Select(Path.GetFileName));
    }

    [Fact]
    public void LaterIncidentReplacesTheSingleRecordInsteadOfAppendingHistory()
    {
        using var directory = new CursorFixture(); var journal = new CursorReloadJournal(Path.Combine(directory.DirectoryPath, "state"));
        journal.Detected(new string('x', 1024)); journal.Completed("Recovered", new string('y', 1024));
        long previousSize = new FileInfo(PathFor(directory)).Length;
        for (int index = 0; index < 20; index++) { journal.Detected("New native divergence"); journal.Completed("Ceded", "Different scheme"); }
        var latest = Read(directory);
        Assert.Equal("New native divergence", latest.Diagnostic); Assert.Equal("Ceded", latest.Outcome);
        Assert.True(new FileInfo(PathFor(directory)).Length < previousSize);
        Assert.Single(Directory.GetFileSystemEntries(directory.DirectoryPath));
    }

    [Fact]
    public void CompletionWithoutAnIncidentDoesNotCreateAnyFile()
    {
        using var directory = new CursorFixture();
        new CursorReloadJournal(Path.Combine(directory.DirectoryPath, "state")).Completed("Recovered", "No incident");
        Assert.Empty(Directory.GetFileSystemEntries(directory.DirectoryPath));
    }

    [Fact]
    public void MissingDiagnosticStillProducesAReadableIncident()
    {
        using var directory = new CursorFixture();
        new CursorReloadJournal(Path.Combine(directory.DirectoryPath, "state")).Detected(null);
        Assert.False(string.IsNullOrWhiteSpace(Read(directory).Diagnostic));
    }

    [Fact]
    public void MalformedPreviousRecordIsReplacedWithoutReadingOrCreatingHistory()
    {
        using var directory = new CursorFixture(); File.WriteAllText(PathFor(directory), "{malformed");
        var journal = new CursorReloadJournal(Path.Combine(directory.DirectoryPath, "state"));
        journal.Detected("Fresh observation"); journal.Completed("Recovered", "Verified");
        Assert.Equal("Fresh observation", Read(directory).Diagnostic);
        Assert.Single(Directory.GetFileSystemEntries(directory.DirectoryPath));
    }

    [Fact]
    public void AnUnavailableLogDirectoryCannotInterruptRecoveryOrTouchOtherFiles()
    {
        using var directory = new CursorFixture();
        string blocked = directory.WriteText("blocked", "ordinary file, not a directory");
        string unrelated = directory.WriteText("unrelated.txt", "preserve");
        var journal = new CursorReloadJournal(Path.Combine(blocked, "state"));
        Assert.Null(Record.Exception(() => journal.Detected("native divergence")));
        Assert.Null(Record.Exception(() => journal.Completed("Recovered", "verified")));
        Assert.Equal("ordinary file, not a directory", File.ReadAllText(blocked));
        Assert.Equal("preserve", File.ReadAllText(unrelated));
        Assert.Equal(2, Directory.GetFileSystemEntries(directory.DirectoryPath).Length);
    }

    [Fact]
    public void PersonalDataRemovalIncludesJournalAndPreservesUnrelatedFiles()
    {
        using var directory = new CursorFixture();
        string workspace = Path.Combine(directory.DirectoryPath, "workspace"), state = Path.Combine(directory.DirectoryPath, "state");
        Directory.CreateDirectory(workspace);
        string config = Path.Combine(workspace, "config.toml"); File.WriteAllText(config, "user configuration");
        string unrelated = directory.WriteText("unrelated.txt", "preserve");
        new CursorReloadJournal(state).Detected("Baseline reloaded");
        int retired = 0;
        var plan = PersonalDataRemoval.Prepare(workspace, state, () => null, _ => retired++);
        Assert.Contains(PathFor(directory), plan.Paths);
        plan.Delete();
        Assert.False(File.Exists(PathFor(directory))); Assert.False(File.Exists(config));
        Assert.Equal("preserve", File.ReadAllText(unrelated)); Assert.Equal(1, retired);
    }

    [Fact]
    public void ChangedJournalAfterConsentPreventsEveryDeletion()
    {
        using var directory = new CursorFixture();
        string workspace = Path.Combine(directory.DirectoryPath, "workspace"), state = Path.Combine(directory.DirectoryPath, "state");
        Directory.CreateDirectory(workspace);
        string config = Path.Combine(workspace, "config.toml"); File.WriteAllText(config, "preserve configuration");
        var journal = new CursorReloadJournal(state); journal.Detected("First observation");
        int retired = 0;
        var plan = PersonalDataRemoval.Prepare(workspace, state, () => null, _ => retired++);
        journal.Completed("Recovered", "Updated after confirmation");
        Assert.Throws<IOException>(plan.Delete);
        Assert.Equal("preserve configuration", File.ReadAllText(config));
        Assert.Equal("Updated after confirmation", Read(directory).Detail); Assert.Equal(0, retired);
    }

    private static string PathFor(CursorFixture directory) => Path.Combine(directory.DirectoryPath, CursorReloadJournal.FileName);
    private static CursorReloadJournal.Incident Read(CursorFixture directory)
        => JsonSerializer.Deserialize<CursorReloadJournal.Incident>(File.ReadAllText(PathFor(directory)))!;
}
