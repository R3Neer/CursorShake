using CursorShake.Windows.Configuration;
namespace CursorShake.App.Dialogs;

internal sealed class DiagnosticListDialog : DialogForm
{
    private readonly DataGridView _items;
    private readonly Label _status, _heading;
    private readonly Action<UiDiagnostic> _open;
    private readonly Button _details, _copy, _close;
    private readonly Dictionary<string, Seen> _seen = new(StringComparer.Ordinal);
    private UiDiagnostic[] _diagnostics = [];
    private AppLanguage _language;
    private bool _copyFailed, _fittingColumns;
    private readonly Func<UiDiagnostic, int>? _occurrenceProvider;
    private readonly Func<UiDiagnostic, DateTimeOffset>? _lastSeenProvider;
    private sealed record Seen(UiDiagnostic Last, DateTimeOffset At, int Count);
    public DiagnosticListDialog(Action<UiDiagnostic> open, AppLanguage language = AppLanguage.English, Func<UiDiagnostic, int>? occurrenceProvider = null, Func<UiDiagnostic, DateTimeOffset>? lastSeenProvider = null)
    {
        ArgumentNullException.ThrowIfNull(open); _open = open; _language = language; _occurrenceProvider = occurrenceProvider; _lastSeenProvider = lastSeenProvider;
        StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(650, 360); Size = new Size(860, 480);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 4, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _heading = DialogStyle.CreateHeading(""); _heading.Dock = DockStyle.Fill; layout.Controls.Add(_heading, 0, 0);
        _status = DialogStyle.CreateBody(""); _status.Dock = DockStyle.Fill; _status.Margin = new Padding(0, 8, 0, 16); layout.Controls.Add(_status, 0, 1);
        _items = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, EnableHeadersVisualStyles = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            BorderStyle = BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        };
        _items.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _items.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _items.DefaultCellStyle.Padding = new Padding(6, 5, 6, 5);
        foreach (var column in new[] { ("Area", 22f, 90), ("Issue", 48f, 160), ("Status", 16f, 70), ("LastSeen", 17f, 85), ("Count", 9f, 45) })
            _items.Columns.Add(new DataGridViewTextBoxColumn { Name = column.Item1, FillWeight = column.Item2, MinimumWidth = column.Item3, SortMode = DataGridViewColumnSortMode.NotSortable });
        _items.SizeChanged += (_, _) => FitColumns();
        _items.ColumnMinimumWidthChanged += (_, _) => FitColumns();
        _items.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) OpenSelection(); }; layout.Controls.Add(_items, 0, 2);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 16, 0, 0) };
        _close = DialogStyle.CreateButton(""); _close.DialogResult = DialogResult.Cancel; _close.Click += (_, _) => Close(); CancelButton = _close; buttons.Controls.Add(_close);
        _copy = DialogStyle.CreateButton(""); _copy.Click += (_, _) => CopyAll(); buttons.Controls.Add(_copy);
        _details = DialogStyle.CreateButton("", true); _details.Enabled = false; _details.Click += (_, _) => OpenSelection(); buttons.Controls.Add(_details);
        layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
        _items.SelectionChanged += (_, _) => _details.Enabled = _items.SelectedRows.Count == 1;
        layout.SizeChanged += (_, _) => { int width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal); _heading.MaximumSize = new Size(width, 0); _status.MaximumSize = new Size(width, 0); };
        SetLanguage(language); DialogStyle.Apply(this);
    }
    public void SetLanguage(AppLanguage language)
    {
        _language = language;
        Text = T("CursorShake — Current issues", "CursorShake — Incidencias actuales");
        _heading.Text = T("Current issues", "Incidencias actuales");
        _items.AccessibleName = _heading.Text;
        string[] columns = [T("Area", "Área"), T("Issue", "Incidencia"), T("Status", "Estado"), T("Last seen", "Última vez"), T("Count", "Veces")];
        for (int index = 0; index < columns.Length; index++) _items.Columns[index].HeaderText = columns[index];
        _close.Text = T("Close", "Cerrar"); _copy.Text = T("Copy details", "Copiar detalles"); _details.Text = T("Review", "Revisar");
        Rebuild();
        DialogStyle.Apply(_items); FitColumns();
    }
    public void SetDiagnostics(IReadOnlyList<UiDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var current = diagnostics.ToArray(); var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in current)
        {
            string key = Key(diagnostic); active.Add(key);
            if (!_seen.TryGetValue(key, out var seen)) _seen[key] = new(diagnostic, DateTimeOffset.UtcNow, 1);
            else if (!ReferenceEquals(seen.Last, diagnostic)) _seen[key] = new(diagnostic, DateTimeOffset.UtcNow, seen.Count + 1);
        }
        foreach (var key in _seen.Keys.Where(key => !active.Contains(key)).ToArray()) _seen.Remove(key);
        _diagnostics = current; _copyFailed = false; Rebuild();
    }
    private void Rebuild()
    {
        var selected = _items.SelectedRows.Count == 1 ? _items.SelectedRows[0].Tag as UiDiagnostic : null;
        _items.SuspendLayout();
        try
        {
            _items.Rows.Clear();
            foreach (var diagnostic in _diagnostics)
            {
                var presentation = DiagnosticText.Present(diagnostic, _language);
                var seen = _seen[Key(diagnostic)];
                int index = _items.Rows.Add(Area(diagnostic.Area), presentation.Title, T("Pending", "Pendiente"),
                    (_lastSeenProvider?.Invoke(diagnostic) ?? seen.At).ToLocalTime().ToString("HH:mm:ss"), (_occurrenceProvider?.Invoke(diagnostic) ?? seen.Count).ToString());
                _items.Rows[index].Tag = diagnostic;
            }
            _items.ClearSelection();
            foreach (DataGridViewRow row in _items.Rows) if (row.Tag is UiDiagnostic item && item == selected) row.Selected = true;
        }
        finally { _items.ResumeLayout(); }
        _status.Text = _copyFailed ? T("The details could not be copied. Review an issue to read them.", "No se han podido copiar los detalles. Abre una incidencia para leerlos.")
            : _diagnostics.Length == 0 ? T("There are no current issues.", "No hay incidencias actuales.")
            : T("Select an issue to see what happened and what you can do next.", "Selecciona una incidencia para ver qué ha pasado y cómo solucionarlo.");
        _details.Enabled = _items.SelectedRows.Count == 1; _copy.Enabled = _diagnostics.Length > 0; FitColumns();
    }
    private void FitColumns()
    {
        if (_fittingColumns || _items.Columns.Count == 0) return;
        _fittingColumns = true;
        try
        {
            // Give measured metadata headers their full width; the description takes the remaining space.
            foreach (DataGridViewColumn column in _items.Columns)
            {
                column.AutoSizeMode = column.Name == "Issue" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None;
                if (column.Name != "Issue") column.Width = column.MinimumWidth;
            }
            var area = _items.Columns["Area"]!;
            var issue = _items.Columns["Issue"]!;
            int otherMinimum = _items.Columns.Cast<DataGridViewColumn>().Where(column => column != area && column != issue).Sum(column => column.MinimumWidth);
            int available = Math.Max(0, _items.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            int areaBudget = Math.Max(area.MinimumWidth, available - otherMinimum - issue.MinimumWidth);
            int contentWidth = _items.Rows.Cast<DataGridViewRow>().Select(row => TextRenderer.MeasureText(row.Cells["Area"].Value?.ToString() ?? "", _items.Font).Width + _items.DefaultCellStyle.Padding.Horizontal + 8).DefaultIfEmpty(area.MinimumWidth).Max();
            area.Width = Math.Clamp(contentWidth, area.MinimumWidth, areaBudget);
        }
        finally { _fittingColumns = false; }
    }
    private void CopyAll()
    {
        if (_diagnostics.Length == 0) return;
        try { Clipboard.SetText(string.Join("\r\n\r\n", _diagnostics.Select(item => item.CopyText(_language)))); }
        catch { _copyFailed = true; Rebuild(); }
    }
    private void OpenSelection() { if (_items.SelectedRows.Count == 1 && _items.SelectedRows[0].Tag is UiDiagnostic diagnostic) _open(diagnostic); }
    private string Area(string area) => area switch
    {
        "Configuración" or "Configuration" => T("Configuration", "Configuración"),
        "Tema" or "theme" or "Theme" => T("Theme", "Tema"),
        "Cursor" or "Motor" => T("Cursor appearance", "Aspecto del cursor"),
        "Inicio" or "Startup" => T("Startup", "Inicio"),
        "Cierre" or "Shutdown" => T("Closing the app", "Cierre de la aplicación"),
        "Recovery" or "Recuperación" => T("Recovery", "Recuperación"),
        "Input" or "Entrada" => T("Mouse input", "Entrada del ratón"),
        _ => T("Application", "Aplicación")
    };
    private string T(string english, string spanish) => DiagnosticText.T(_language, english, spanish);
    private static string Key(UiDiagnostic value) => string.Join("\0", value.Area, value.Code, value.Path, value.Summary, value.Details);
}
