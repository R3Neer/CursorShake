using System.Runtime.InteropServices;
using CursorShake.App.Appearance;
namespace CursorShake.App.Tray;

/// <summary>Tray presentation and commands only. All access belongs to the WinForms thread.</summary>
public sealed class TrayView : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly NotifyIcon _notify;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _themes = new("Tema");
    private readonly ToolStripMenuItem _pause, _startup, _errors;
    private readonly Icon _normalIcon, _pausedIcon, _errorIcon;
    private readonly ExplorerMessages _messages;
    private string _themeSignature = "";
    private bool _disposed;
    public event Action<TrayAction>? ActionRequested;

    public TrayView(Icon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        _normalIcon = (Icon)icon.Clone();
        _pausedIcon = CreateBadge(icon, false); _errorIcon = CreateBadge(icon, true);
        _menu.Items.Add(_status); _menu.Items.Add(new ToolStripSeparator());
        Add("Abrir configuración", TrayActionKind.OpenConfiguration);
        Add("Ubicación de configuración…", TrayActionKind.ChooseWorkspace);
        Add("Abrir carpeta de configuración", TrayActionKind.OpenWorkspace);
        _menu.Items.Add(new ToolStripSeparator()); _menu.Items.Add(_themes);
        Add("Probar crecimiento", TrayActionKind.Preview);
        _pause = Add("Pausar crecimiento", TrayActionKind.TogglePause);
        _startup = Add("Iniciar con Windows", TrayActionKind.ToggleStartup);
        _menu.Items.Add(new ToolStripSeparator());
        _errors = Add("Ver errores…", TrayActionKind.ShowErrors);
        Add("Salir", TrayActionKind.Exit);
        RefreshAppearance();
        _menu.Opening+=(_,_)=>RefreshAppearance();
        _menu.Opened+=(_,_)=>WindowsAppearance.PreferRoundedCorners(_menu);
        _themes.DropDownOpening+=(_,_)=>WindowsAppearance.StyleMenu(_themes.DropDown);
        _themes.DropDownOpened+=(_,_)=>WindowsAppearance.PreferRoundedCorners(_themes.DropDown);
        _notify = new NotifyIcon { Icon = _normalIcon, Text = "CursorShake", ContextMenuStrip = _menu, Visible = true };
        _notify.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) Request(TrayActionKind.OpenConfiguration); };
        _messages = new ExplorerMessages(() => { if (!_disposed) { _notify.Visible = false; _notify.Visible = true; } });
        Update(new TrayState());
    }
    public void Update(TrayState state)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(state);
        _status.Text = state.StatusText;
        _pause.Text = state.Paused ? "Reanudar crecimiento" : "Pausar crecimiento";
        _pause.Checked = state.Paused; _startup.Checked = state.StartupEnabled;
        _errors.Text = state.HasErrors ? "Ver errores… (hay incidencias)" : "Ver errores…";
        _notify.Icon = state.HasErrors ? _errorIcon : state.Paused ? _pausedIcon : _normalIcon;
        string tooltip = "CursorShake — " + state.StatusText;
        int maximum = Math.Min(63, tooltip.Length);
        if (maximum > 0 && char.IsHighSurrogate(tooltip[maximum - 1])) maximum--;
        _notify.Text = tooltip[..maximum];
        _themes.Text = state.PendingTheme is null ? "Tema" : "Tema (cambio pendiente)";
        string signature = string.Join("\0", state.Themes.Select(x => x.Key + "=" + x.Value));
        if (_themeSignature != signature)
        {
            _themeSignature = signature;
            while (_themes.DropDownItems.Count > 0) { var item = _themes.DropDownItems[0]; _themes.DropDownItems.RemoveAt(0); item.Dispose(); }
            foreach (var entry in state.Themes)
            {
                string id = entry.Key;
                var item = new ToolStripMenuItem(entry.Value.Replace("&", "&&", StringComparison.Ordinal)) { Tag = id };
                item.Click += (_, _) => Request(TrayActionKind.SelectTheme, id);
                _themes.DropDownItems.Add(item);
            }
        }
        _themes.Enabled = _themes.DropDownItems.Count > 0;
        foreach (ToolStripMenuItem item in _themes.DropDownItems)
        {
            string id = (string)item.Tag!;
            item.Checked = string.Equals(id, state.ThemeId, StringComparison.OrdinalIgnoreCase);
        }
    }
    public void RefreshAppearance()=>WindowsAppearance.StyleMenu(_menu);
    private ToolStripMenuItem Add(string label, TrayActionKind kind)
    {
        var item = new ToolStripMenuItem(label); item.Click += (_, _) => Request(kind); _menu.Items.Add(item); return item;
    }
    private void Request(TrayActionKind kind, string? theme = null) { if (!_disposed) ActionRequested?.Invoke(new(kind, theme)); }
    private void RequireThread() { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Tray access must run on its UI thread."); }
    public void Dispose()
    {
        RequireThread(); if (_disposed) return; _disposed = true;
        _notify.Visible = false; _messages.Dispose(); _notify.Dispose(); _menu.Dispose();
        _normalIcon.Dispose(); _pausedIcon.Dispose(); _errorIcon.Dispose();
    }
    private static Icon CreateBadge(Icon source, bool error)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent); graphics.DrawIcon(source, new Rectangle(0, 0, 32, 32));
            using var background = new SolidBrush(error ? SystemColors.Info : SystemColors.Control);
            using var pen = new Pen(error ? SystemColors.InfoText : SystemColors.ControlText, 2);
            graphics.FillEllipse(background, 18, 18, 13, 13); graphics.DrawEllipse(pen, 18, 18, 13, 13);
            if (error) { graphics.DrawLine(pen, 24, 21, 24, 25); graphics.DrawLine(pen, 24, 28, 24, 28.5f); }
            else { graphics.DrawLine(pen, 22, 22, 22, 28); graphics.DrawLine(pen, 27, 22, 27, 28); }
        }
        nint handle = bitmap.GetHicon();
        try { using var temporary = Icon.FromHandle(handle); return (Icon)temporary.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(nint icon);
    private sealed class ExplorerMessages : NativeWindow, IDisposable
    {
        private readonly Action _recreate;
        private readonly uint _message;
        public ExplorerMessages(Action recreate)
        {
            _recreate = recreate; _message = RegisterWindowMessage("TaskbarCreated");
            // Broadcast messages do not reach HWND_MESSAGE: use an invisible tool window instead.
            CreateHandle(new CreateParams { Caption = "CursorShake tray lifecycle", Style = unchecked((int)0x80000000), ExStyle = 0x80, Width = 0, Height = 0 });
        }
        protected override void WndProc(ref Message m) { if ((uint)m.Msg == _message && _message != 0) _recreate(); base.WndProc(ref m); }
        public void Dispose() => DestroyHandle();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    }
}
