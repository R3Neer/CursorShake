using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
namespace CursorShake.Windows.Configuration;

public sealed record WorkspaceProgress(int CompletedFiles, int TotalFiles, string Path);

public sealed class WorkspaceStore
{
    private readonly string _stateDirectory;
    private readonly LocatorStore _locator;
    private readonly string _mutexName;
    private readonly Action<WorkspaceCheckpoint>? _checkpoint;
    private readonly List<WorkspaceDiagnostic> _notices = [];
    private readonly object _sync = new();
    private bool _authorityUnknown;
    public string RootPath { get; private set; }
    public string ConfigPath => Path.Combine(RootPath, "config.toml");
    public string ThemesPath => Path.Combine(RootPath, "themes");
    public long Generation { get; private set; }
    public IReadOnlyList<WorkspaceDiagnostic> RecoveryNotices => _notices.AsReadOnly();
    public bool CleanupPending => _locator.Read()?.Phase == "CleanupPending";
    public event Action? RootChanged;
    public event Action<WorkspaceDiagnostic>? Diagnostic;
    public event Action<WorkspaceProgress>? Progress;

    public WorkspaceStore(string initialRoot, string stateDirectory, bool persistLocator = true, Action<WorkspaceCheckpoint>? checkpoint = null)
    {
        _stateDirectory = WorkspacePaths.Canonical(stateDirectory); _checkpoint = checkpoint;
        using (DirectoryProtection.Acquire(_stateDirectory, true)) { }
        string identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        _mutexName = "Global\\CursorShake.Workspace." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + _stateDirectory.ToUpperInvariant())))[..24];
        _locator = new LocatorStore(_stateDirectory, persistLocator);
        using var operation = Acquire();
        var current = _locator.Read();
        RootPath = WorkspacePaths.Canonical(current?.Root ?? initialRoot);
        if (current is null)
        {
            using (DirectoryProtection.Acquire(RootPath, true)) { }
            _locator.Write(new WorkspaceLocator(RootPath));
        }
        RecoverUnderLock();
    }

    public string EnsureConfiguration()
    {
        lock (_sync)
        {
            using var operation = Acquire(); RefreshAuthority();
            using var parents = DirectoryProtection.Acquire(RootPath, true);
            try { using var existing = ProtectedFile.Open(ConfigPath); return existing.ReadText(); }
            catch (FileNotFoundException)
            {
                if (CleanupPending || _notices.Any(n => n.Code == "RecoveryRequired")) throw new ConfigurationException("RecoveryRequired", "Configuration is absent during an unfinished operation; resolve preserved versions first.", ConfigPath);
                try
                {
                    using var created = ProtectedFile.Open(ConfigPath, FileAccess.ReadWrite, FileMode.CreateNew);
                    var bytes = Encoding.UTF8.GetBytes(ConfigurationDefaults.Text);
                    created.Stream.Write(bytes); created.Stream.Flush(true);
                    return ConfigurationDefaults.Text;
                }
                catch (IOException) when (File.Exists(ConfigPath)) { using var appeared = ProtectedFile.Open(ConfigPath); return appeared.ReadText(); }
            }
        }
    }

    public ConfigurationSnapshot Read()
    {
        lock (_sync)
        {
            using var operation = Acquire(); RefreshAuthority();
            using var file = ProtectedFile.Open(ConfigPath);
            return new ConfigurationSnapshot(file.ReadText(), file.Hash());
        }
    }

    public ConfigurationSnapshot ProtectedUpdate(Func<string, string> update, string expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_sync)
        {
            using var operation = Acquire(); RefreshAuthority();
            if (CleanupPending || Directory.EnumerateFiles(_stateDirectory, "*.write.json").Any()) throw new ConfigurationException("RecoveryRequired", "Resolve the unfinished operation before writing configuration.", ConfigPath);
            using var file = ProtectedFile.Open(ConfigPath, FileAccess.ReadWrite);
            string previousHash = file.Hash();
            if (!string.Equals(previousHash, expectedRevision, StringComparison.Ordinal)) throw new ConfigurationException("Conflict", "Configuration changed since it was read; reload before editing.", ConfigPath);
            byte[] before = file.ReadBytes(); string previous = ProtectedFile.Decode(before);
            ConfigParser.Parse(previous, ConfigPath);
            string candidate = update(previous) ?? throw new ArgumentException("Update returned null.", nameof(update));
            ConfigParser.Parse(candidate, ConfigPath);
            byte[] after = Encoding.UTF8.GetBytes(candidate);
            string candidateHash = Convert.ToHexString(SHA256.HashData(after));
            if (candidateHash == previousHash) return new(candidate, previousHash);
            string id = Guid.NewGuid().ToString("N");
            string beforePath = Path.Combine(_stateDirectory, id + ".before.toml"), candidatePath = Path.Combine(_stateDirectory, id + ".candidate.toml");
            WriteVersion(beforePath, before); WriteVersion(candidatePath, after);
            string journalPath = Path.Combine(_stateDirectory, id + ".write.json");
            DurableState.Write(journalPath, new WriteJournal(id, ConfigPath, file.Identity, previousHash, candidateHash, beforePath, candidatePath));
            _checkpoint?.Invoke(WorkspaceCheckpoint.WriteJournalDurable);
            file.Stream.Position = 0; file.Stream.SetLength(0);
            _checkpoint?.Invoke(WorkspaceCheckpoint.WriteStarted);
            file.Stream.Write(after); file.Stream.SetLength(after.Length); file.Stream.Flush(true);
            if (file.Hash() != candidateHash) throw new ConfigurationException("RecoveryRequired", "Configuration verification failed after protected write.", ConfigPath);
            _checkpoint?.Invoke(WorkspaceCheckpoint.WriteFlushed);
            DeleteInternal(journalPath); DeleteInternal(beforePath); DeleteInternal(candidatePath);
            return new(candidate, candidateHash);
        }
    }

    public WorkspaceMoveResult MoveTo(string destination, Action<string>? prepareDestination = null)
    {
        lock (_sync)
        {
            using var operation = Acquire(); RefreshAuthority();
            destination = WorkspacePaths.Canonical(destination);
            if (WorkspacePaths.Same(destination, RootPath)) return new(RootPath, CleanupPending);
            if (WorkspacePaths.Inside(destination, RootPath) || WorkspacePaths.Inside(RootPath, destination)) throw new ConfigurationException("InvalidDestination", "Source and destination cannot be nested.", destination);
            if (CleanupPending || Directory.EnumerateFiles(_stateDirectory, "*.write.json").Any()) throw new ConfigurationException("RecoveryRequired", "Resolve the current operation before moving the workspace.", RootPath);
            MoveJournal? journal = null; bool committed = false, commitUnknown = false;
            try
            {
                using var sourceProtection = DirectoryProtection.Acquire(RootPath);
                using var destinationProtection = DirectoryProtection.Acquire(destination, true);
                if (File.Exists(Path.Combine(destination, "config.toml")) || Directory.Exists(Path.Combine(destination, "themes")) || File.Exists(Path.Combine(destination, "themes"))) throw new ConfigurationException("DestinationConflict", "Destination already contains configuration or personal themes; choose another folder.", destination);
                using var directories = new ProtectionSet();
                var files = new List<string> { "config.toml" }; var relativeDirectories = new List<string>();
                if (Directory.Exists(ThemesPath)) EnumerateThemeTree(ThemesPath, files, relativeDirectories, directories);
                var moveDirectories = new List<MoveDirectory>(); var entries = new List<MoveEntry>();
                string id = Guid.NewGuid().ToString("N");
                journal = new MoveJournal(id, RootPath, destination, "Preparing", entries, moveDirectories);
                foreach (var relative in relativeDirectories.OrderBy(s => s.Length))
                {
                    string target = WorkspacePaths.Child(destination, relative);
                    using (DirectoryProtection.Acquire(target, true)) { }
                    moveDirectories.Add(new(relative, WorkspaceNative.DirectoryIdentity(WorkspacePaths.Child(RootPath, relative)), WorkspaceNative.DirectoryIdentity(target)));
                }
                string journalPath = Path.Combine(_stateDirectory, id + ".move.json"); DurableState.Write(journalPath, journal);
                foreach (string relative in files)
                {
                    string source = WorkspacePaths.Child(RootPath, relative), target = WorkspacePaths.Child(destination, relative);
                    using var original = ProtectedFile.Open(source);
                    string hash = original.Hash();
                    using var copy = ProtectedFile.Open(target, FileAccess.ReadWrite, FileMode.CreateNew, true);
                    try { original.Stream.CopyTo(copy.Stream); copy.Stream.Flush(true); if (copy.Hash() != hash) throw new IOException("Copied content differs: " + target); }
                    catch { copy.DeleteSameIdentity(); throw; }
                    entries.Add(new(relative, original.Identity, copy.Identity, hash));
                    DurableState.Write(journalPath, journal);
                    Progress?.Invoke(new(entries.Count, files.Count, source));
                }
                _checkpoint?.Invoke(WorkspaceCheckpoint.MoveCopied);
                using var protectedEntries = VerifyEntries(journal);
                ConfigParser.Parse(File.ReadAllText(Path.Combine(destination, "config.toml")), Path.Combine(destination, "config.toml"));
                prepareDestination?.Invoke(destination);
                journal = journal with { Phase = "Prepared" }; DurableState.Write(journalPath, journal);
                _checkpoint?.Invoke(WorkspaceCheckpoint.MovePrepared);
                try
                {
                    _locator.Write(new WorkspaceLocator(destination, id, "CleanupPending"), () => _checkpoint?.Invoke(WorkspaceCheckpoint.LocatorWrittenBeforeFlush));
                    committed = true;
                }
                catch
                {
                    try
                    {
                        var observed = _locator.Read();
                        if (observed?.OperationId == id && WorkspacePaths.Same(observed.Root, destination)) committed = true;
                        else if (observed is null || !WorkspacePaths.Same(observed.Root, journal.Source) || observed.OperationId is not null) commitUnknown = true;
                    }
                    catch { commitUnknown = true; }
                    if (committed) { RootPath = destination; Generation++; }
                    if (commitUnknown)
                    {
                        _authorityUnknown = true;
                        Notice(new("RecoveryRequired", "The workspace commit outcome cannot be read; both locations and the journal were preserved.", journalPath, [journal.Source, destination]));
                    }
                    throw;
                }
                RootPath = destination; Generation++;
                journal = journal with { Phase = "CleanupPending" }; DurableState.Write(journalPath, journal);
                _checkpoint?.Invoke(WorkspaceCheckpoint.RootCommitted);
                return new(RootPath, true);
            }
            catch
            {
                if (!committed && !commitUnknown && journal is not null)
                {
                    if (TryCleanupCopiedDestination(journal)) DeleteInternal(Path.Combine(_stateDirectory, journal.Id + ".move.json"));
                    else Notice(new("RecoveryRequired", "Preparation failed; changed or busy destination copies were preserved.", journal.Destination));
                }
                throw;
            }
            finally { if (committed) NotifyRootChanged(); }
        }
    }

    /// <summary>Call only after the destination configuration/resources are effective and old leases released.</summary>
    public bool CompletePendingMove()
    {
        lock (_sync)
        {
            using var operation = Acquire(); RefreshAuthority(); var locator = _locator.Read();
            if (locator?.OperationId is null || locator.Phase != "CleanupPending") return true;
            string journalPath = Path.Combine(_stateDirectory, locator.OperationId + ".move.json");
            var journal = DurableState.Read<MoveJournal>(journalPath);
            if (journal.Id != locator.OperationId || !WorkspacePaths.Same(journal.Destination, RootPath)) throw new ConfigurationException("RecoveryRequired", "Move journal does not match the durable workspace authority.", journalPath);
            _checkpoint?.Invoke(WorkspaceCheckpoint.CleanupStarted);
            bool complete = true;
            foreach (var entry in journal.Entries)
            {
                string source = WorkspacePaths.Child(journal.Source, entry.RelativePath), destination = WorkspacePaths.Child(journal.Destination, entry.RelativePath);
                try
                {
                    using var target = ProtectedFile.Open(destination);
                    if (target.Identity != entry.DestinationIdentity || target.Hash() != entry.Hash) throw new ConfigurationException("Conflict", "Destination changed after commit; preserving original.", source);
                    try
                    {
                        using var original = ProtectedFile.Open(source, deleteAccess: true);
                        if (original.Identity != entry.SourceIdentity || original.Hash() != entry.Hash) throw new ConfigurationException("Conflict", "Original changed after commit; preserving it.", source);
                        original.DeleteSameIdentity();
                    }
                    catch (FileNotFoundException) { }
                }
                catch (Exception e) when (e is IOException or ConfigurationException or UnauthorizedAccessException)
                { complete = false; Notice(new("CleanupPending", e.Message, source)); }
            }
            foreach (var directory in journal.Directories.OrderByDescending(d => d.RelativePath.Length))
            {
                string source = WorkspacePaths.Child(journal.Source, directory.RelativePath);
                try { WorkspaceNative.RemoveEmptyDirectory(source, directory.SourceIdentity); }
                catch (FileNotFoundException) { }
                catch (Exception e) when (e is IOException or ConfigurationException or UnauthorizedAccessException) { complete = false; Notice(new("CleanupPending", e.Message, source)); }
            }
            if (!complete) return false;
            _locator.Write(new WorkspaceLocator(RootPath)); DeleteInternal(journalPath);
            return true;
        }
    }

    public IReadOnlyList<WorkspaceDiagnostic> RecoverPendingOperations()
    { lock (_sync) { using var operation = Acquire(); RefreshAuthority(); _notices.Clear(); RecoverUnderLock(); return RecoveryNotices; } }

    public void AcceptRecoveredConfiguration(string expectedRevision)
    {
        lock (_sync)
        {
            using var operation = Acquire(); RefreshAuthority(); using var file = ProtectedFile.Open(ConfigPath);
            if (file.Hash() != expectedRevision) throw new ConfigurationException("Conflict", "Configuration changed during recovery.", ConfigPath);
            ConfigParser.Parse(file.ReadText(), ConfigPath);
            foreach (var path in Directory.EnumerateFiles(_stateDirectory, "*.write.json"))
            {
                var journal = DurableState.Read<WriteJournal>(path);
                if (!WorkspacePaths.Same(journal.Path, ConfigPath)) continue;
                DeleteInternal(path); DeleteInternal(journal.BeforePath); DeleteInternal(journal.CandidatePath);
            }
            _notices.Clear();
        }
    }

    private void RecoverUnderLock()
    {
        foreach (string path in Directory.EnumerateFiles(_stateDirectory, "*.write.json"))
        {
            try
            {
                var journal = DurableState.Read<WriteJournal>(path);
                using var current = ProtectedFile.Open(journal.Path);
                string hash = current.Hash();
                if (current.Identity == journal.Identity && (hash == journal.BeforeHash || hash == journal.CandidateHash))
                { DeleteInternal(path); DeleteInternal(journal.BeforePath); DeleteInternal(journal.CandidatePath); }
                else Notice(new("RecoveryRequired", "Interrupted write conflicts with current configuration; no content was overwritten.", journal.Path, [journal.BeforePath, journal.CandidatePath]));
            }
            catch (Exception e) when (e is IOException or ConfigurationException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { Notice(new("RecoveryRequired", e.Message, path)); }
        }
        var locator = _locator.Read();
        foreach (string path in Directory.EnumerateFiles(_stateDirectory, "*.move.json"))
        {
            try
            {
                var journal = DurableState.Read<MoveJournal>(path);
                if (locator?.OperationId == journal.Id && locator.Phase == "CleanupPending")
                { RootPath = WorkspacePaths.Canonical(locator.Root); continue; }
                if (WorkspacePaths.Same(journal.Source, RootPath))
                {
                    if (TryCleanupCopiedDestination(journal)) DeleteInternal(path);
                    else Notice(new("RecoveryRequired", "Uncommitted destination contains changed/busy copies; source remains authoritative.", journal.Destination));
                }
                else Notice(new("RecoveryRequired", "Orphaned move operation requires explicit resolution.", path));
            }
            catch (Exception e) when (e is IOException or ConfigurationException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { Notice(new("RecoveryRequired", e.Message, path)); }
        }
    }

    private static ProtectionSet VerifyEntries(MoveJournal journal)
    {
        var result = new ProtectionSet();
        try
        {
            foreach (var entry in journal.Entries)
            {
                var source = ProtectedFile.Open(WorkspacePaths.Child(journal.Source, entry.RelativePath)); result.Add(source);
                var target = ProtectedFile.Open(WorkspacePaths.Child(journal.Destination, entry.RelativePath)); result.Add(target);
                if (source.Identity != entry.SourceIdentity || target.Identity != entry.DestinationIdentity || source.Hash() != entry.Hash || target.Hash() != entry.Hash) throw new ConfigurationException("Conflict", "Workspace content changed during move preparation.", source.Path);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private void EnumerateThemeTree(string directory, List<string> files, List<string> directories, ProtectionSet protections)
    {
        protections.Add(DirectoryProtection.Acquire(directory));
        directories.Add(Path.GetRelativePath(RootPath, directory));
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (files.Count + directories.Count > 10000) throw new ConfigurationException("LimitExceeded", "Workspace contains too many managed entries for one move.", path);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ConfigurationException("UnsafePath", "Personal themes cannot contain links/reparse points.", path);
            if ((attributes & FileAttributes.Directory) != 0) EnumerateThemeTree(path, files, directories, protections);
            else files.Add(Path.GetRelativePath(RootPath, path));
        }
    }
    private bool TryCleanupCopiedDestination(MoveJournal journal)
    {
        bool complete = true;
        foreach (var entry in journal.Entries)
        {
            string path = WorkspacePaths.Child(journal.Destination, entry.RelativePath);
            try { using var file = ProtectedFile.Open(path, deleteAccess: true); if (file.Identity != entry.DestinationIdentity || file.Hash() != entry.Hash) { complete = false; continue; } file.DeleteSameIdentity(); }
            catch (FileNotFoundException) { }
            catch (Exception e) when (e is IOException or ConfigurationException or UnauthorizedAccessException) { complete = false; }
        }
        foreach (var directory in journal.Directories.OrderByDescending(d => d.RelativePath.Length))
        {
            try { WorkspaceNative.RemoveEmptyDirectory(WorkspacePaths.Child(journal.Destination, directory.RelativePath), directory.DestinationIdentity); }
            catch (FileNotFoundException) { }
            catch (Exception e) when (e is IOException or ConfigurationException or UnauthorizedAccessException) { complete = false; }
        }
        return complete;
    }
    private void RefreshAuthority()
    {
        WorkspaceLocator? locator;
        try { locator = _locator.Read(); }
        catch (Exception e)
        {
            _authorityUnknown = true;
            throw new ConfigurationException("RecoveryRequired", "Cannot determine the durable workspace authority; no files were changed.", _stateDirectory, inner: e);
        }
        if (locator is null)
        {
            if (_authorityUnknown) throw new ConfigurationException("RecoveryRequired", "The durable workspace locator is absent after an ambiguous commit.", _stateDirectory);
            return;
        }
        _authorityUnknown = false;
        string root = WorkspacePaths.Canonical(locator.Root);
        if (!WorkspacePaths.Same(RootPath, root)) { RootPath = root; Generation++; NotifyRootChanged(); }
    }
    private void NotifyRootChanged()
    {
        if (RootChanged is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Notice(new("RootNotificationFailed", "The new root is authoritative but notification failed: " + e.Message, RootPath)); }
        }
    }
    private IDisposable Acquire()
    {
        var mutex = new Mutex(false, _mutexName); bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); throw new ConfigurationException("Busy", "Another session is changing this workspace.", RootPath); }
        return new MutexLease(mutex);
    }
    private void Notice(WorkspaceDiagnostic notice) { if (!_notices.Contains(notice)) _notices.Add(notice); Diagnostic?.Invoke(notice); }
    private static void WriteVersion(string path, byte[] bytes) { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); file.Write(bytes); file.Flush(true); }
    private void DeleteInternal(string path)
    {
        if (!WorkspacePaths.Inside(path, _stateDirectory)) throw new ConfigurationException("UnsafePath", "Recovery metadata escapes internal state.", path);
        if (File.Exists(path)) { using var file = ProtectedFile.Open(path, deleteAccess: true); file.DeleteSameIdentity(); }
    }
    private sealed class MutexLease(Mutex mutex) : IDisposable { public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); } }
    private sealed class ProtectionSet : IDisposable
    {
        private readonly List<IDisposable> _items = [];
        public void Add(IDisposable item) => _items.Add(item);
        public void Dispose() { for (int i = _items.Count - 1; i >= 0; i--) _items[i].Dispose(); _items.Clear(); }
    }
}
