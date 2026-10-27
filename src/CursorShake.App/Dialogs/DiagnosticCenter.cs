using CursorShake.Windows.Configuration;
namespace CursorShake.App.Dialogs;

/// <summary>Bounded current diagnostics with one popup per distinct failure, owned by the UI thread.</summary>
public sealed class DiagnosticCenter : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Action<string>? _openPath;
    private readonly Action? _retry;
    private readonly Func<Task>? _retryAsync;
    private readonly Func<AppLanguage> _language;
    private readonly Dictionary<DiagnosticKey,(int Count,DateTimeOffset Last)> _occurrences=[];
    private readonly Action<UiDiagnostic>? _presenter;
    private readonly List<UiDiagnostic> _entries = [];
    private readonly Dictionary<DiagnosticKey, DiagnosticDialog> _popups = [];
    private readonly Dictionary<DiagnosticKey, ConfigurationRecoveryDialog> _recoveries = [];
    private readonly HashSet<Task> _operations = [];
    private readonly HashSet<ConfigurationRecoveryDialog> _retiredRecoveries=[];
    private bool _stopping;
    private DiagnosticListDialog? _list;
    private bool _disposed;
    public event Action? Changed;
    public Func<UiDiagnostic, RecoveryDialogContext?>? RecoveryResolver { get; set; }
    public Func<UiDiagnostic, DiagnosticAction?>? ActionResolver { get; set; }
    public Task PendingOperations => Task.WhenAll(_operations.Select(ObserveCompletion).ToArray());
    public bool HasErrors => _entries.Count > 0;
    public IReadOnlyList<UiDiagnostic> Current => _entries.AsReadOnly();
    public DiagnosticCenter(Action<string>? openPath = null, Action? retry = null, Action<UiDiagnostic>? presenter = null, Func<AppLanguage>? language = null, Func<Task>? retryAsync = null)
    { _openPath = openPath; _retry = retry; _presenter = presenter; _language=language??(()=>AppLanguage.English);_retryAsync=retryAsync; }

    public void Report(UiDiagnostic diagnostic)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(diagnostic);
        var key = Key(diagnostic);
        if(_occurrences.TryGetValue(key,out var seen))
        {
            _occurrences[key]=(seen.Count+1,DateTimeOffset.UtcNow);
            int index=_entries.FindIndex(entry=>Key(entry)==key);
            if(index>=0)
            {
                var previous=_entries[index];_entries[index]=diagnostic;
                if(!(previous.RelatedPaths??[]).SequenceEqual(diagnostic.RelatedPaths??[],StringComparer.OrdinalIgnoreCase)&&_recoveries.Remove(key,out var obsolete)){_retiredRecoveries.Add(obsolete);obsolete.InvalidateContext();}
            }
            if(_popups.TryGetValue(key,out var popup)&&!popup.IsDisposed)
            {var actions=ActionsFor(diagnostic);popup.UpdateDiagnostic(diagnostic,actions.Recovery,actions.Extra);}
            _list?.SetDiagnostics(_entries);Changed?.Invoke();return;
        }
        _occurrences[key]=(1,DateTimeOffset.UtcNow);
        if (_entries.Count == 50)
        {
            var oldest = Key(_entries[0]); _entries.RemoveAt(0);_occurrences.Remove(oldest);
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
        foreach (var entry in removed){_occurrences.Remove(Key(entry));if (_popups.Remove(Key(entry), out var popup)) popup.Close();}
        _list?.SetDiagnostics(_entries); Changed?.Invoke();
    }
    public void ShowErrors()
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_list is { IsDisposed: false }) { _list.Activate(); return; }
        _list = new DiagnosticListDialog(OpenDiagnostic,_language(),d=>_occurrences.TryGetValue(Key(d),out var seen)?seen.Count:1,d=>_occurrences.TryGetValue(Key(d),out var seen)?seen.Last:DateTimeOffset.UtcNow);
        _list.FormClosed += (_, _) => _list = null;
        _list.SetDiagnostics(_entries); _list.Show();
    }
    private void OpenDiagnostic(UiDiagnostic diagnostic)
    {
        var key = Key(diagnostic);
        if (_popups.TryGetValue(key, out var existing) && !existing.IsDisposed) { existing.Activate(); return; }
        var actions=ActionsFor(diagnostic);
        var dialog = new DiagnosticDialog(diagnostic,_openPath,_retry,actions.Recovery,actions.Extra,_language(),_retryAsync is null?null:InvokeRetryAsync);
        _popups[key] = dialog;
        dialog.FormClosed += (_, _) => { if (_popups.TryGetValue(key, out var current) && ReferenceEquals(current, dialog)) _popups.Remove(key); };
        if (_list is { IsDisposed: false }) dialog.Show(_list); else dialog.Show();
    }
    private (Action? Recovery,DiagnosticAction? Extra) ActionsFor(UiDiagnostic diagnostic)
    {
        var context=RecoveryResolver?.Invoke(diagnostic);
        Action? resolve=context is null?null:()=>OpenRecovery(diagnostic,context);
        var extra=ActionResolver?.Invoke(diagnostic);
        if(extra is not null)extra=extra with{Execute=WrapAction(extra.Execute)};
        return(resolve,extra);
    }
    private Task InvokeRetryAsync()=>Track(_retryAsync??(()=>Task.CompletedTask));
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
        var dialog = new ConfigurationRecoveryDialog(tracked, _openPath,_language());
        _recoveries[key] = dialog;
        dialog.FormClosed += (_, _) => {if(_recoveries.TryGetValue(key,out var current)&&ReferenceEquals(current,dialog))_recoveries.Remove(key);_retiredRecoveries.Remove(dialog);};
        if (_popups.TryGetValue(key, out var popup) && !popup.IsDisposed) dialog.Show(popup); else dialog.Show();
    }
    private Func<Task> WrapAction(Func<Task> action) => () => Track(action);
    private async Task<T> Track<T>(Func<Task<T>> action)
    {
        if (_stopping || _disposed) throw new InvalidOperationException(DiagnosticText.T(_language(),"The application is closing.","La aplicación se está cerrando."));
        var task = action(); _operations.Add(task);
        try { return await task; } finally { _operations.Remove(task); }
    }
    private async Task Track(Func<Task> action)
    {
        if (_stopping || _disposed) throw new InvalidOperationException(DiagnosticText.T(_language(),"The application is closing.","La aplicación se está cerrando."));
        var task = action(); _operations.Add(task);
        try { await task; } finally { _operations.Remove(task); }
    }
    private static async Task ObserveCompletion(Task task) { try { await task; } catch { } }
    public Task BeginShutdownAsync()
    {
        RequireThread(); _stopping = true;
        foreach (var dialog in _recoveries.Values.Concat(_retiredRecoveries).ToArray()) dialog.InvalidateContext();
        return PendingOperations;
    }
    private void RequireThread() { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Diagnostic presentation must run on its UI thread."); }
    private static DiagnosticKey Key(UiDiagnostic diagnostic) => new(diagnostic.Area, diagnostic.Code, diagnostic.Path?.ToUpperInvariant(), diagnostic.Line,diagnostic.Column);
    private readonly record struct DiagnosticKey(string Area, string Code, string? Path, int? Line,int? Column);
    public void RefreshLanguage()
    {
        RequireThread();if(_disposed)return;var language=_language();
        foreach(var popup in _popups.Values)if(!popup.IsDisposed){var actions=ActionsFor(popup.Diagnostic);popup.UpdateDiagnostic(popup.Diagnostic,actions.Recovery,actions.Extra);popup.SetLanguage(language);}
        foreach(var recovery in _recoveries.Values.Concat(_retiredRecoveries).ToArray())if(!recovery.IsDisposed)recovery.SetLanguage(language);
        _list?.SetLanguage(language);
    }
    public void Dispose()
    {
        RequireThread(); if (_disposed) return; _disposed = true;
        foreach (var recovery in _recoveries.Values.Concat(_retiredRecoveries).ToArray()) recovery.Dispose();
        _recoveries.Clear();_retiredRecoveries.Clear();
        foreach (var popup in _popups.Values.ToArray()) popup.Close();
        _popups.Clear(); _list?.Close(); _list = null; _entries.Clear();_occurrences.Clear();
    }
}
