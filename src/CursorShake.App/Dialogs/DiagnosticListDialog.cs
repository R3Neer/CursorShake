namespace CursorShake.App.Dialogs;

internal sealed class DiagnosticListDialog : Form
{
    private readonly ListView _items;
    private readonly Label _status;
    private readonly Action<UiDiagnostic> _open;
    private readonly Button _details;
    private UiDiagnostic[] _diagnostics = [];
    public DiagnosticListDialog(Action<UiDiagnostic> open)
    {
        _open = open; Text = "CursorShake — Incidencias actuales";
        Font = SystemFonts.MessageBoxFont ?? Control.DefaultFont; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(620, 340); Size = new Size(840, 460);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _status = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        layout.Controls.Add(_status, 0, 0);
        _items = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, AccessibleName = "Incidencias actuales" };
        _items.Columns.Add("Área", 130); _items.Columns.Add("Mensaje", 360); _items.Columns.Add("Ubicación", 280);
        _items.DoubleClick += (_, _) => OpenSelection();
        layout.Controls.Add(_items, 0, 1);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
        var close = new Button { Text = "&Cerrar", AutoSize = true, DialogResult = DialogResult.Cancel }; close.Click += (_, _) => Close(); CancelButton = close; buttons.Controls.Add(close);
        var copy = new Button { Text = "&Copiar todas", AutoSize = true }; copy.Click += (_, _) => { if (_diagnostics.Length > 0) { try { Clipboard.SetText(string.Join("\r\n\r\n", _diagnostics.Select(d => d.CopyText()))); } catch (Exception e) { MessageBox.Show(this, e.Message, "No se pudieron copiar los detalles", MessageBoxButtons.OK, MessageBoxIcon.Error); } } }; buttons.Controls.Add(copy);
        _details = new Button { Text = "Ver &detalles", AutoSize = true, Enabled = false }; _details.Click += (_, _) => OpenSelection(); buttons.Controls.Add(_details);
        layout.Controls.Add(buttons, 0, 2); Controls.Add(layout);
        _items.SelectedIndexChanged += (_, _) => _details.Enabled = _items.SelectedItems.Count == 1;
    }
    public void SetDiagnostics(IReadOnlyList<UiDiagnostic> diagnostics)
    {
        var selected = _items.SelectedItems.Count == 1 ? _items.SelectedItems[0].Tag as UiDiagnostic : null;
        _diagnostics = diagnostics.ToArray(); _items.BeginUpdate();
        try
        {
            _items.Items.Clear();
            foreach (var diagnostic in _diagnostics)
            {
                var item = new ListViewItem([diagnostic.Area, diagnostic.Summary, diagnostic.Path ?? ""]) { Tag = diagnostic };
                _items.Items.Add(item); if (diagnostic == selected) item.Selected = true;
            }
        }
        finally { _items.EndUpdate(); }
        _status.Text = diagnostics.Count == 0 ? "No hay incidencias actuales." : $"{diagnostics.Count} incidencia(s). Selecciona una para consultar sus detalles.";
        _details.Enabled = _items.SelectedItems.Count == 1;
    }
    private void OpenSelection() { if (_items.SelectedItems.Count == 1 && _items.SelectedItems[0].Tag is UiDiagnostic diagnostic) _open(diagnostic); }
}
