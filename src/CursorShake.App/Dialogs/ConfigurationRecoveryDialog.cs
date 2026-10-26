namespace CursorShake.App.Dialogs;

/// <summary>Reviews preserved paths and a specific current revision; all file work is delegated to the coordinator.</summary>
public sealed class ConfigurationRecoveryDialog : Form
{
    private readonly RecoveryDialogContext _context;
    private readonly Action<string>? _openPath;
    private readonly ListBox _paths;
    private readonly TextBox _preview;
    private readonly Label _status;
    private readonly Button _refresh, _keep;
    private RecoveryReview? _review;
    private bool _busy;
    public ConfigurationRecoveryDialog(RecoveryDialogContext context, Action<string>? openPath = null)
    {
        ArgumentNullException.ThrowIfNull(context); _context = context; _openPath = openPath;
        Text = "CursorShake — Resolver configuración pendiente";
        Font = SystemFonts.MessageBoxFont ?? Control.DefaultFont; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(660, 460); Size = new Size(860, 620);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var explanation = new Label { Text = "Abre las versiones conservadas si necesitas compararlas. Puedes editar el archivo actual y volver a comprobarlo. Conservar la versión actual retira sus temporales de recuperación, sin sobrescribir el TOML.", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8) };
        layout.Controls.Add(explanation, 0, 0);
        _paths = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, AccessibleName = "Archivo actual y versiones conservadas" };
        foreach (string path in new[] { context.CurrentPath }.Concat(context.PreservedPaths).Distinct(StringComparer.OrdinalIgnoreCase)) _paths.Items.Add(path);
        if (_paths.Items.Count > 0) _paths.SelectedIndex = 0;
        layout.Controls.Add(_paths, 0, 1);
        var previewLabel = new Label { Text = "Contenido actual revisado", AutoSize = true, Padding = new Padding(0, 8, 0, 4) }; layout.Controls.Add(previewLabel, 0, 2);
        _preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, AccessibleName = "Contenido actual revisado" }; layout.Controls.Add(_preview, 0, 3);
        _status = new Label { Text = "Comprobando archivo actual…", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) }; layout.Controls.Add(_status, 0, 4);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Margin = Padding.Empty };
        var close = new Button { Text = "&Cerrar", AutoSize = true, DialogResult = DialogResult.Cancel }; close.Click += (_, _) => Close(); CancelButton = close; buttons.Controls.Add(close);
        _keep = new Button { Text = "Conservar configuración &actual…", AutoSize = true, Enabled = false }; _keep.Click += async (_, _) => await KeepAsync(); buttons.Controls.Add(_keep);
        _refresh = new Button { Text = "Volver a &comprobar", AutoSize = true }; _refresh.Click += async (_, _) => await RefreshAsync(); buttons.Controls.Add(_refresh);
        if (openPath is not null)
        {
            var open = new Button { Text = "&Abrir seleccionado", AutoSize = true }; open.Click += (_, _) => OpenSelected(); buttons.Controls.Add(open);
            _paths.DoubleClick += (_, _) => OpenSelected();
        }
        layout.Controls.Add(buttons, 0, 5); Controls.Add(layout);
        void BoundLabels()
        {
            int width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal - explanation.Margin.Horizontal);
            explanation.MaximumSize = new Size(width, 0);
            _status.MaximumSize = new Size(width, 0);
        }
        layout.SizeChanged += (_, _) => BoundLabels(); BoundLabels();
        Shown += async (_, _) => await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        if (_busy || IsDisposed) return;
        _busy = true; SetBusy(true);
        try
        {
            var review = await _context.ReviewCurrent();
            if (IsDisposed) return;
            _review = review; _preview.Text = review.Text;
            _status.Text = review.CanKeepCurrent ? "La configuración actual es válida. Puedes conservar esta revisión explícitamente." : "No se puede conservar todavía: " + (review.ValidationError ?? "la validación no se ha completado.");
        }
        catch (Exception error)
        {
            if (!IsDisposed) { _review = null; _status.Text = "No se ha podido comprobar el archivo: " + error.Message; }
        }
        finally { _busy = false; if (!IsDisposed) SetBusy(false); }
    }
    private async Task KeepAsync()
    {
        if (_busy || _review is not { CanKeepCurrent: true } review) return;
        if (MessageBox.Show(this, "Se conservará la revisión actual que aparece en esta ventana y se retirarán sus versiones temporales de recuperación. El TOML no se sobrescribirá. ¿Continuar?", "Confirmar configuración conservada", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _busy = true; SetBusy(true);
        try
        {
            await _context.KeepCurrent(review.Revision);
            _context.Resolved?.Invoke();
            if (!IsDisposed) Close();
        }
        catch (Exception error)
        {
            if (!IsDisposed) { _review = null; _status.Text = "No se ha cerrado la recuperación: " + error.Message + " Vuelve a comprobar el archivo antes de intentarlo otra vez."; }
        }
        finally { _busy = false; if (!IsDisposed) SetBusy(false); }
    }
    private void SetBusy(bool busy) { _refresh.Enabled = !busy; _keep.Enabled = !busy && _review?.CanKeepCurrent == true; }
    private void OpenSelected()
    {
        if (_paths.SelectedItem is not string path || _openPath is null) return;
        try { _openPath(path); } catch (Exception error) { MessageBox.Show(this, error.Message, "No se pudo abrir el archivo", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
