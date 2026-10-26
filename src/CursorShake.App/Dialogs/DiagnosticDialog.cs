namespace CursorShake.App.Dialogs;

public sealed class DiagnosticDialog : Form
{
    private readonly Font _headingFont;
    private readonly Bitmap _errorImage;
    private bool _actionRunning;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<DiagnosticAction, bool>? ConfirmAction { get; set; }
    public UiDiagnostic Diagnostic { get; }
    public DiagnosticDialog(UiDiagnostic diagnostic, Action<string>? openPath = null, Action? retry = null, Action? resolveRecovery = null, DiagnosticAction? additionalAction = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostic); Diagnostic = diagnostic;
        Text = "CursorShake — " + diagnostic.Area;
        Font = SystemFonts.MessageBoxFont ?? Control.DefaultFont; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(560, 380); Size = new Size(760, 520);
        ShowInTaskbar = true; MinimizeBox = true;
        _headingFont = new Font(Font, FontStyle.Bold);
        _errorImage = SystemIcons.Error.ToBitmap();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var icon = new PictureBox { Image = _errorImage, SizeMode = PictureBoxSizeMode.CenterImage, Size = new Size(32, 32), Anchor = AnchorStyles.Top | AnchorStyles.Left, TabStop = false };
        layout.Controls.Add(icon, 0, 0);
        var summary = new Label { Text = diagnostic.Summary, UseMnemonic = false, Font = _headingFont, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 8) };
        layout.Controls.Add(summary, 1, 0);
        if (diagnostic.Path is { } path)
        {
            var location = new TextBox { Text = path + (diagnostic.Line is null ? "" : $":{diagnostic.Line}:{diagnostic.Column ?? 1}"), ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Ubicación del error", Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(location, 0, 1); layout.SetColumnSpan(location, 2);
        }
        var detailsLabel = new Label { Text = "Detalles", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        layout.Controls.Add(detailsLabel, 0, 2); layout.SetColumnSpan(detailsLabel, 2);
        var details = new TextBox { Text = diagnostic.Details, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, AccessibleName = "Detalles del error", Margin = new Padding(0, 0, 0, 12) };
        layout.Controls.Add(details, 0, 3); layout.SetColumnSpan(details, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Margin = Padding.Empty };
        var close = new Button { Text = "&Cerrar", AutoSize = true, DialogResult = DialogResult.Cancel }; close.Click += (_, _) => Close(); buttons.Controls.Add(close); CancelButton = close;
        var copy = new Button { Text = "&Copiar detalles", AutoSize = true }; copy.Click += (_, _) => RunAction(() => Clipboard.SetText(diagnostic.CopyText())); buttons.Controls.Add(copy);
        if (retry is not null)
        {
            var repeat = new Button { Text = "&Reintentar", AutoSize = true }; repeat.Click += (_, _) => RunAction(retry); buttons.Controls.Add(repeat);
        }
        if (resolveRecovery is not null)
        {
            var resolve = new Button { Text = "Resolver &conflicto…", AutoSize = true }; resolve.Click += (_, _) => RunAction(resolveRecovery); buttons.Controls.Add(resolve);
        }
        if (additionalAction is not null)
        {
            var actionButton = new Button { Text = additionalAction.Label, AutoSize = true };
            actionButton.Click += async (_, _) => await RunAdditionalAction(additionalAction, actionButton);
            buttons.Controls.Add(actionButton);
        }
        if (diagnostic.Path is { } file && openPath is not null)
        {
            var open = new Button { Text = "&Abrir ubicación", AutoSize = true }; open.Click += (_, _) => RunAction(() => openPath(file)); buttons.Controls.Add(open);
        }
        layout.Controls.Add(buttons, 0, 4); layout.SetColumnSpan(buttons, 2); Controls.Add(layout);
        void BoundSummary() => summary.MaximumSize = new Size(Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal - 40 - summary.Margin.Horizontal), 0);
        layout.SizeChanged += (_, _) => BoundSummary(); BoundSummary();
    }
    private async Task RunAdditionalAction(DiagnosticAction action, Button button)
    {
        if (_actionRunning || IsDisposed) return;
        bool confirmed = ConfirmAction?.Invoke(action) ?? MessageBox.Show(this, action.ConfirmationText,
            "Confirmar acción", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        if (!confirmed) return;
        _actionRunning = true; button.Enabled = false;
        try { await action.Execute(); }
        catch (Exception error)
        {
            if (!IsDisposed) MessageBox.Show(this, error.Message, "No se pudo completar la acción", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _actionRunning = false; if (!IsDisposed) button.Enabled = true; }
    }
    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "No se pudo completar la acción", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _headingFont.Dispose(); _errorImage.Dispose(); }
        base.Dispose(disposing);
    }
}
