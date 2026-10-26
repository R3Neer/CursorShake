namespace CursorShake.Windows.Configuration;

/// <summary>Debounced invalidation signals. The coordinator reads, validates and rejects stale results.</summary>
public sealed class FileMonitor : IDisposable
{
    private readonly object _sync = new();
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _timer;
    private readonly TimeSpan _delay;
    private string _root = "";
    private bool _disposed;
    private int _generation;
    public event Action? Changed;
    public event Action<Exception>? Error;
    public FileMonitor(string root, TimeSpan? debounce = null)
    {
        _delay = debounce ?? TimeSpan.FromMilliseconds(150);
        if (_delay < TimeSpan.Zero || _delay > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(debounce));
        ReconfigureRoot(root);
    }
    public void ReconfigureRoot(string root)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _generation++; _watcher?.Dispose(); _timer?.Dispose();
            _root = WorkspacePaths.Canonical(root);
            int generation = _generation;
            _timer = new System.Threading.Timer(_ => Publish(generation), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 32768
            };
            _watcher.Changed += OnChange; _watcher.Created += OnChange; _watcher.Deleted += OnChange;
            _watcher.Renamed += (_, e) => { if (Relevant(e.FullPath) || Relevant(e.OldFullPath)) Schedule(); };
            _watcher.Error += (_, e) => { Error?.Invoke(e.GetException()); Schedule(); };
            _watcher.EnableRaisingEvents = true;
        }
    }
    private void OnChange(object sender, FileSystemEventArgs e) { if (Relevant(e.FullPath)) Schedule(); }
    private bool Relevant(string path)
    {
        lock (_sync)
        {
            if (_disposed) return false;
            return WorkspacePaths.Same(path, Path.Combine(_root, "config.toml")) || WorkspacePaths.Same(path, Path.Combine(_root, "themes")) || WorkspacePaths.Inside(path, Path.Combine(_root, "themes"));
        }
    }
    private void Schedule()
    {
        lock (_sync) { if (!_disposed) _timer?.Change(_delay, Timeout.InfiniteTimeSpan); }
    }
    private void Publish(int generation)
    {
        lock (_sync) { if (_disposed || generation != _generation) return; }
        try { Changed?.Invoke(); } catch (Exception e) { Error?.Invoke(e); }
    }
    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; _generation++; _watcher?.Dispose(); _timer?.Dispose(); }
    }
}
