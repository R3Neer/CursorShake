namespace CursorShake.App.Dialogs;

/// <summary>Bounded current diagnostics with one popup per distinct failure, owned by the UI thread.</summary>
public sealed class DiagnosticCenter : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Action<string>? _openPath;
    private readonly Action? _retry;
    private readonly Action<UiDiagnostic>? _presenter;
    private readonly List<UiDiagnostic> _entries = [];
    private readonly Dictionary<DiagnosticKey, DiagnosticDialog> _popups = [];
    private readonly Dictionary<DiagnosticKey, ConfigurationRecoveryDialog> _recoveries = [];
    private readonly HashSet<Task> _operations = [];
    private bool _stopping;
    private DiagnosticListDialog? _list;
    private bool _disposed;
    public event Action? Changed;
    public Func<UiDiagnostic, RecoveryDialogContext?>? RecoveryResolver { get; set; }
    public Func<UiDiagnostic, DiagnosticAction?>? ActionResolver { get; set; }
    public Task PendingOperations => Task.WhenAll(_operations.Select(ObserveCompletion).ToArray());
    public bool HasErrors => _entries.Count > 0;
    public IReadOnlyList<UiDiagnostic> Current => _entries.AsReadOnly();
    public DiagnosticCenter(Action<string>? openPath = null, Action? retry = null, Action<UiDiagnostic>? presenter = null)
    { _openPath = openPath; _retry = retry; _presenter = presenter; }

    public void Report(UiDiagnostic diagnostic)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(diagnostic);
        var key = Key(diagnostic);
        if (_entries.Any(entry => Key(entry) == key)) return;
        if (_entries.Count == 50)
        {
            var oldest = Key(_entries[0]); _entries.RemoveAt(0);
            if (_popups.Remove(oldest, out var popup)) popup.Close();
        }
        _entries.Add(diagnostic); _list?.SetDiagnostics(_entries); Changed?.Invoke();
        if (_presenter is not null) _presenter(diagnostic); else OpenDiagnostic(diagnostic);
    }
    public void ClearArea(string area)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        var removed = _entries.Where(entry => string.Equals(entry.Area, area, StringComparison.Ordinal)).ToArray();
        if (removed.Length == 0) return;
        _entries.RemoveAll(entry => string.Equals(entry.Area, area, StringComparison.Ordinal));
        foreach (var entry in removed) if (_popups.Remove(Key(entry), out var popup)) popup.Close();
        _list?.SetDiagnostics(_entries); Changed?.Invoke();
    }
    public void ShowErrors()
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_list is { IsDisposed: false }) { _list.Activate(); return; }
        _list = new DiagnosticListDialog(OpenDiagnostic);
        _list.FormClosed += (_, _) => _list = null;
        _list.SetDiagnostics(_entries); _list.Show();
    }
    private void OpenDiagnostic(UiDiagnostic diagnostic)
    {
        var key = Key(diagnostic);
        if (_popups.TryGetValue(key, out var existing) && !existing.IsDisposed) { existing.Activate(); return; }
        var context = RecoveryResolver?.Invoke(diagnostic);
        Action? resolve = context is null ? null : () => OpenRecovery(diagnostic, context);
        var extraAction = ActionResolver?.Invoke(diagnostic);
        if (extraAction is not null) extraAction = extraAction with { Execute = WrapAction(extraAction.Execute) };
        var dialog = new DiagnosticDialog(diagnostic, _openPath, _retry, resolve, extraAction);
        _popups[key] = dialog;
        dialog.FormClosed += (_, _) => { if (_popups.TryGetValue(key, out var current) && ReferenceEquals(current, dialog)) _popups.Remove(key); };
        if (_list is { IsDisposed: false }) dialog.Show(_list); else dialog.Show();
    }
    private void OpenRecovery(UiDiagnostic diagnostic, RecoveryDialogContext context)
    {
        if (_disposed || _stopping) return;
        var key = Key(diagnostic);
        if (_recoveries.TryGetValue(key, out var existing) && !existing.IsDisposed) { existing.Activate(); return; }
        var tracked = context with
        {
            ReviewCurrent = () => Track(context.ReviewCurrent),
            KeepCurrent = revision => Track(() => context.KeepCurrent(revision))
        };
        var dialog = new ConfigurationRecoveryDialog(tracked, _openPath);
        _recoveries[key] = dialog;
        dialog.FormClosed += (_, _) => _recoveries.Remove(key);
        if (_popups.TryGetValue(key, out var popup) && !popup.IsDisposed) dialog.Show(popup); else dialog.Show();
    }
    private Func<Task> WrapAction(Func<Task> action) => () => Track(action);
    private async Task<T> Track<T>(Func<Task<T>> action)
    {
        if (_stopping || _disposed) throw new InvalidOperationException("La aplicación se está cerrando.");
        var task = action(); _operations.Add(task);
        try { return await task; } finally { _operations.Remove(task); }
    }
    private async Task Track(Func<Task> action)
    {
        if (_stopping || _disposed) throw new InvalidOperationException("La aplicación se está cerrando.");
        var task = action(); _operations.Add(task);
        try { await task; } finally { _operations.Remove(task); }
    }
    private static async Task ObserveCompletion(Task task) { try { await task; } catch { } }
    public Task BeginShutdownAsync()
    {
        RequireThread(); _stopping = true;
        foreach (var dialog in _recoveries.Values.ToArray()) dialog.Close();
        return PendingOperations;
    }
    private void RequireThread() { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Diagnostic presentation must run on its UI thread."); }
    private static DiagnosticKey Key(UiDiagnostic diagnostic) => new(diagnostic.Area, diagnostic.Code, diagnostic.Path?.ToUpperInvariant(), diagnostic.Details);
    private readonly record struct DiagnosticKey(string Area, string Code, string? Path, string Details);
    public void Dispose()
    {
        RequireThread(); if (_disposed) return; _disposed = true;
        foreach (var recovery in _recoveries.Values.ToArray()) recovery.Close();
        _recoveries.Clear();
        foreach (var popup in _popups.Values.ToArray()) popup.Close();
        _popups.Clear(); _list?.Close(); _list = null; _entries.Clear();
    }
}
