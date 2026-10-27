using CursorShake.Windows.Configuration;
namespace CursorShake.App.Dialogs;

/// <summary>Reviews a specific current revision; all file operations remain delegated to the coordinator.</summary>
public sealed class ConfigurationRecoveryDialog : DialogForm
{
    private readonly RecoveryDialogContext _context;
    private readonly Action<string>? _openPath;
    private readonly ListBox _paths;
    private readonly TextBox _preview;
    private readonly Label _status, _heading, _explanation;
    private readonly Button _refresh, _keep, _close;
    private readonly LinkLabel _technical;
    private readonly Button? _open;
    private readonly Panel _technicalPanel, _bodyScroll;
    private readonly FlowLayoutPanel _footer;
    private RecoveryReview? _review;
    private bool _busy, _expanded, _contextInvalid, _measuringBody;
    private string? _failureDetails;
    private AppLanguage _language;
    private ReviewState _state = ReviewState.Waiting;
    private enum ReviewState { Waiting, Checking, Valid, Invalid, ReviewFailed, Keeping, KeepFailed, OpenFailed, ContextChanged }
    private sealed record PathChoice(string Path, bool Current, int Number, string Label) { public override string ToString() => Label; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<bool>? ConfirmKeep { get; set; }
    public ConfigurationRecoveryDialog(RecoveryDialogContext context, Action<string>? openPath = null, AppLanguage language = AppLanguage.English)
    {
        ArgumentNullException.ThrowIfNull(context); _context = context; _openPath = openPath; _language = language;
        StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(680, 480); Size = new Size(850, 620);
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 2 };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bodyScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = false, Padding = Padding.Empty, Margin = Padding.Empty, ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bodyScroll.Controls.Add(layout); outer.Controls.Add(_bodyScroll, 0, 0);
        _heading = DialogStyle.CreateHeading(""); _heading.Dock = DockStyle.Fill; layout.Controls.Add(_heading, 0, 0);
        _explanation = DialogStyle.CreateBody(""); _explanation.Dock = DockStyle.Fill; _explanation.Margin = new Padding(0, 12, 0, 16); layout.Controls.Add(_explanation, 0, 1);
        var files = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        files.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); files.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        files.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        _paths = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = false, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty };
        foreach (string path in new[] { context.CurrentPath }.Concat(context.PreservedPaths).Distinct(StringComparer.OrdinalIgnoreCase))
            _paths.Items.Add(new PathChoice(path, string.Equals(path, context.CurrentPath, StringComparison.OrdinalIgnoreCase), _paths.Items.Count, ""));
        if (_paths.Items.Count > 0) _paths.SelectedIndex = 0;
        files.Controls.Add(_paths, 0, 0);
        if (openPath is not null)
        {
            _open = DialogStyle.CreateButton(""); _open.Anchor = AnchorStyles.Top | AnchorStyles.Right; _open.Margin = new Padding(12, 0, 0, 0);
            _open.Click += (_, _) => OpenSelected(); files.Controls.Add(_open, 1, 0);
            _paths.DoubleClick += (_, _) => OpenSelected();
        }
        layout.Controls.Add(files, 0, 2);
        var validation = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 12, 0, 8) };
        validation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); validation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        validation.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _status = DialogStyle.CreateBody(""); _status.Dock = DockStyle.Fill; _status.Margin = Padding.Empty; validation.Controls.Add(_status, 0, 0);
        _refresh = DialogStyle.CreateButton(""); _refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right; _refresh.Margin = new Padding(12, 0, 0, 0);
        _refresh.Click += async (_, _) => await RefreshAsync(); validation.Controls.Add(_refresh, 1, 0); layout.Controls.Add(validation, 0, 3);
        _technical = new LinkLabel { AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, UseMnemonic = false, Margin = new Padding(0, 8, 0, 8) };
        layout.Controls.Add(_technical, 0, 4);
        _technicalPanel = new Panel { Dock = DockStyle.Fill, Height = 140, MinimumSize = new Size(0, 140), Visible = false, Margin = new Padding(0, 4, 0, 8) };
        _preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        _technicalPanel.Controls.Add(_preview); layout.Controls.Add(_technicalPanel, 0, 5);
        _technical.LinkClicked += (_, _) => { _expanded = !_expanded; _technicalPanel.Visible = _expanded; RefreshLabels(); };
        var buttons = _footer = new RecoveryFooter { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 16, 0, 0), Padding = Padding.Empty };
        _close = DialogStyle.CreateButton(""); _close.TabIndex = 1; _close.DialogResult = DialogResult.Cancel; _close.Margin = new Padding(12, 0, 0, 0); _close.Click += (_, _) => Close(); CancelButton = _close; buttons.Controls.Add(_close);
        _keep = DialogStyle.CreateButton("", true); _keep.TabIndex = 0; _keep.Enabled = false; _keep.Margin = Padding.Empty; _keep.Click += async (_, _) => await KeepAsync(); buttons.Controls.Add(_keep);
        outer.Controls.Add(buttons, 0, 1); Controls.Add(outer);
        _bodyScroll.SizeChanged += (_, _) => BoundContent();
        layout.Layout += (_, _) => BoundContent();
        FontChanged += (_, _) => BoundContent();
        Shown += async (_, _) => await ReviewOnOpenAsync();
        FormClosing += (_, args) => { if (_busy && args.CloseReason == CloseReason.UserClosing) args.Cancel = true; };
        SetLanguage(language); DialogStyle.Apply(this);
    }
    public void SetLanguage(AppLanguage language) { _language = language; RefreshLabels(); }
    internal Task ReviewOnOpenAsync() => RefreshAsync();
    internal void InvalidateContext()
    {
        if (IsDisposed) return;
        _contextInvalid = true; _review = null; _state = ReviewState.ContextChanged; SetBusy(_busy); _preview.Clear(); RefreshLabels();
        if (!_busy) Close();
    }
    private void BoundContent()
    {
        if (_measuringBody || _refresh is null || _bodyScroll.Controls.Count == 0) return;
        _measuringBody = true;
        try
        {
            int width = Math.Max(1, _bodyScroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            var body = (TableLayoutPanel)_bodyScroll.Controls[0];
            body.MaximumSize = new Size(width, 0);
            _heading.MaximumSize = new Size(width, 0); _explanation.MaximumSize = new Size(width, 0);
            _status.MaximumSize = new Size(Math.Max(1, width - _refresh.GetPreferredSize(Size.Empty).Width - _refresh.Margin.Horizontal), 0);
            var visibleRows = body.Controls.Cast<Control>().Where(control => control != _technicalPanel || _expanded).ToArray();
            int height = visibleRows.Sum(control => control.GetPreferredSize(new Size(Math.Max(1, width - control.Margin.Horizontal), 0)).Height + control.Margin.Vertical);
            body.Height = Math.Max(1, height + body.Padding.Vertical);
            body.PerformLayout();
            body.Height = Math.Max(body.Height, visibleRows.Max(control => control.Bottom + control.Margin.Bottom) + body.Padding.Bottom);
            _bodyScroll.AutoScrollMinSize = new Size(0, body.Height);
        }
        finally { _measuringBody = false; }
    }    private void RefreshLabels()
    {
        Text = T("CursorShake — Review configuration", "CursorShake — Revisar configuración");
        _heading.Text = T("Review configuration", "Revisar configuración");
        _explanation.Text = T("1. Open the current file and preserved copies to compare them.\n2. Edit the current file if needed, then check it again.\n3. Keep the current file when it is valid. Its recovery copies will be removed.", "1. Abre el archivo actual y las copias conservadas para compararlos.\n2. Edita el archivo actual si lo necesitas y vuelve a comprobarlo.\n3. Conserva el archivo actual cuando sea válido. Se retirarán sus copias de recuperación.");
        _paths.AccessibleName = T("Current configuration and preserved copies", "Configuración actual y copias conservadas");
        int selected = _paths.SelectedIndex;
        for (int index = 0; index < _paths.Items.Count; index++)
        {
            var path = (PathChoice)_paths.Items[index];
            string label = path.Current ? T("Current configuration", "Configuración actual") : T("Preserved copy ", "Copia conservada ") + path.Number;
            _paths.Items[index] = path with { Label = label };
        }
        if (selected >= 0) _paths.SelectedIndex = selected;
        _close.Text = T("Close", "Cerrar"); _refresh.Text = T("Check", "Comprobar"); _keep.Text = T("Keep", "Conservar");
        if (_open is not null) _open.Text = T("Open", "Abrir");
        _technical.Text = _expanded ? T("Hide details", "Ocultar detalles") : T("View details", "Ver detalles");
        _preview.AccessibleName = T("Technical details and reviewed content", "Detalles técnicos y contenido revisado");
        _status.Text = _state switch
        {
            ReviewState.Waiting => T("Check the current file before keeping a version.", "Comprueba el archivo actual antes de conservar una versión."),
            ReviewState.Checking => T("Checking the current configuration…", "Comprobando la configuración actual…"),
            ReviewState.Valid => T("The current configuration is valid. You can keep the version you just reviewed.", "La configuración actual es válida. Puedes conservar la versión que acabas de revisar."),
            ReviewState.Invalid => T("The current configuration needs corrections. Open it, make your changes, then check again.", "La configuración actual necesita correcciones. Ábrela, realiza los cambios y vuelve a comprobarla."),
            ReviewState.ReviewFailed => T("The file could not be checked. Try again or review the technical details.", "No se ha podido comprobar el archivo. Inténtalo de nuevo o consulta los detalles técnicos."),
            ReviewState.Keeping => T("Keeping the reviewed configuration…", "Conservando la configuración revisada…"),
            ReviewState.ContextChanged => T("The configuration changed. Finishing the current operation…", "La configuración ha cambiado. Terminando la operación actual…"),
            ReviewState.KeepFailed => T("The recovery could not be completed. Check the current file again before retrying.", "No se ha podido completar la recuperación. Vuelve a comprobar el archivo actual antes de intentarlo de nuevo."),
            _ => T("The selected file could not be opened. You can try again or review the technical details.", "No se ha podido abrir el archivo seleccionado. Puedes intentarlo de nuevo o consultar los detalles técnicos.")
        };
        BoundContent();
    }
    private async Task RefreshAsync()
    {
        if (_busy || IsDisposed || _contextInvalid) return;
        _busy = true; _state = ReviewState.Checking; SetBusy(true); RefreshLabels();
        try
        {
            var review = await _context.ReviewCurrent();
            if (IsDisposed || _contextInvalid) return;
            _review = review; _failureDetails = review.ValidationError;
            _preview.Text = TechnicalText(review.Text, _failureDetails);
            _state = review.CanKeepCurrent ? ReviewState.Valid : ReviewState.Invalid;
        }
        catch (Exception error)
        {
            if (!IsDisposed && !_contextInvalid) { _review = null; _failureDetails = error.ToString(); _preview.Text = TechnicalText(null, _failureDetails); _state = ReviewState.ReviewFailed; }
        }
        finally { _busy = false; if (!IsDisposed) { SetBusy(false); if (_contextInvalid) Close(); else RefreshLabels(); } }
    }
    private async Task KeepAsync()
    {
        if (_busy || IsDisposed || _contextInvalid || _review is not { CanKeepCurrent: true } review) return;
        _busy = true; SetBusy(true);
        try
        {
            bool confirmed = ConfirmKeep?.Invoke() ?? DialogStyle.Confirm(this,
                T("Keep this configuration?", "¿Conservar esta configuración?"),
                T("Keep the version you just reviewed and remove its temporary recovery copies? The current configuration file will stay as it is.", "¿Conservar la versión que acabas de revisar y retirar sus copias temporales de recuperación? El archivo de configuración actual quedará tal como está."),
                T("Keep", "Conservar"), _language);
            if (!confirmed || IsDisposed || _contextInvalid) return;
            _state = ReviewState.Keeping; RefreshLabels();
            await _context.KeepCurrent(review.Revision);
            if (IsDisposed || _contextInvalid) return;
            _context.Resolved?.Invoke();
            _busy = false; Close();
        }
        catch (Exception error)
        {
            if (!IsDisposed && !_contextInvalid) { _review = null; _failureDetails = error.ToString(); _preview.Text = TechnicalText(null, _failureDetails); _state = ReviewState.KeepFailed; }
        }
        finally { _busy = false; if (!IsDisposed) { SetBusy(false); if (_contextInvalid) Close(); else RefreshLabels(); } }
    }
    private void SetBusy(bool busy) { _refresh.Enabled = !busy && !_contextInvalid; _keep.Enabled = !busy && !_contextInvalid && _review?.CanKeepCurrent == true; if (_open is not null) _open.Enabled = !busy && !_contextInvalid; }
    private void OpenSelected()
    {
        if (_busy || _contextInvalid || _paths.SelectedItem is not PathChoice path || _openPath is null) return;
        try { _openPath(path.Path); }
        catch (Exception error) { _failureDetails = error.ToString(); _preview.Text = TechnicalText(_review?.Text, _failureDetails); _state = ReviewState.OpenFailed; RefreshLabels(); }
    }
    private string TechnicalText(string? content, string? error)
        => string.Join("\r\n\r\n", new[] { _context.CurrentPath, content, error }.Where(value => !string.IsNullOrEmpty(value)));
    private sealed class RecoveryFooter : FlowLayoutPanel
    {
        public override Size GetPreferredSize(Size proposedSize)
        {
            var preferred = base.GetPreferredSize(proposedSize);
            // Hidden forms and inherited-font changes can make FlowLayout report a 1px row.
            // Reserve the actual buttons' intrinsic height even before their handles exist.
            int height = Controls.Cast<Control>().Select(control => control.GetPreferredSize(Size.Empty).Height + control.Margin.Vertical).DefaultIfEmpty(0).Max() + Padding.Vertical;
            return new Size(preferred.Width, Math.Max(preferred.Height, height));
        }
    }
    private string T(string english, string spanish) => DiagnosticText.T(_language, english, spanish);
}
