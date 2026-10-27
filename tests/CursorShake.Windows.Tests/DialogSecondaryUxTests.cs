using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using CursorShake.App.Dialogs;
using CursorShake.Windows.Configuration;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class DialogSecondaryUxTests
{
    [Fact]
    public void IssueListUsesHumanTitlesAndMetadataWithoutMakingPathsOrCodesThePrimaryContent() => Sta(() =>
    {
        var issue = new UiDiagnostic("Configuración", "InvalidToml", "raw parser exception", "System.InvalidOperationException: private details", @"D:\private\config.toml", 9, 2);
        using var dialog = new DiagnosticListDialog(_ => { }, occurrenceProvider: _ => 4, lastSeenProvider: _ => new DateTimeOffset(2026, 10, 27, 10, 11, 12, TimeSpan.Zero));
        dialog.SetDiagnostics([issue]); var items = Field<DataGridView>(dialog, "_items");
        Assert.Single(items.Rows.Cast<DataGridViewRow>()); Assert.Equal(DiagnosticText.Present(issue, AppLanguage.English).Title, items.Rows[0].Cells[1].Value);
        Assert.Equal("4", items.Rows[0].Cells[4].Value);
        string visible = string.Join(" ", items.Rows[0].Cells.Cast<DataGridViewCell>().Select(item => item.Value?.ToString()));
        Assert.DoesNotContain(issue.Path!, visible); Assert.DoesNotContain(issue.Code, visible); Assert.DoesNotContain("Exception", visible);
        Assert.Equal("Configuration", items.Rows[0].Cells[0].Value);
        dialog.SetLanguage(AppLanguage.Spanish);
        Assert.Equal("Incidencia", items.Columns[1].HeaderText); Assert.Equal("Configuración", items.Rows[0].Cells[0].Value);
        Assert.Equal(DiagnosticText.Present(issue, AppLanguage.Spanish).Title, items.Rows[0].Cells[1].Value);
        Assert.Equal("4", items.Rows[0].Cells[4].Value);
    });

    [Fact]
    public void FallbackOccurrenceCountDoesNotIncreaseForRefreshOrLanguageChange() => Sta(() =>
    {
        var issue = new UiDiagnostic("Cursor", "RendererFailed", "summary", "technical");
        using var dialog = new DiagnosticListDialog(_ => { });
        dialog.SetDiagnostics([issue]); dialog.SetDiagnostics([issue]); dialog.SetLanguage(AppLanguage.Spanish);
        var list = Field<DataGridView>(dialog, "_items"); Assert.Equal("1", list.Rows[0].Cells[4].Value);
        dialog.SetDiagnostics([issue with { }]); Assert.Equal("2", list.Rows[0].Cells[4].Value);
        dialog.SetDiagnostics([]); Assert.Empty(list.Rows.Cast<DataGridViewRow>()); Assert.False(Field<Button>(dialog, "_copy").Enabled);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryKeepsReviewedRevisionOnlyAfterAValidReviewAndExplicitConfirmation(bool confirmed) => Sta(() =>
    {
        int calls = 0, confirmations = 0;
        using var dialog = NewRecovery(true, revision => { Assert.Equal("revision-A", revision); calls++; return Task.CompletedTask; });
        dialog.ConfirmKeep = () => { confirmations++; return confirmed; };
        Finish(Invoke(dialog, "RefreshAsync"));
        Assert.True(Field<Button>(dialog, "_keep").Enabled);
        Assert.False(Field<Panel>(dialog, "_technicalPanel").Visible);
        Finish(Invoke(dialog, "KeepAsync"));
        Assert.Equal(1, confirmations); Assert.Equal(confirmed ? 1 : 0, calls);
    });

    [Fact]
    public void InvalidReviewCannotInvokeConfirmationOrKeepAndTechnicalErrorIsCollapsed() => Sta(() =>
    {
        int calls = 0;
        using var dialog = NewRecovery(false, _ => { calls++; return Task.CompletedTask; });
        dialog.ConfirmKeep = () => throw new InvalidOperationException("Confirmation must not open.");
        Finish(Invoke(dialog, "RefreshAsync")); Finish(Invoke(dialog, "KeepAsync"));
        Assert.False(Field<Button>(dialog, "_keep").Enabled); Assert.Equal(0, calls);
        Assert.DoesNotContain("private validation details", Field<Label>(dialog, "_status").Text);
        Assert.Contains("private validation details", Field<TextBox>(dialog, "_preview").Text);
        Assert.False(Field<Panel>(dialog, "_technicalPanel").Visible);
    });

    [Fact]
    public void PendingKeepCannotBeReenteredAndFailureRequiresANewReview() => Sta(() =>
    {
        int calls = 0; var pending = new TaskCompletionSource();
        using var dialog = NewRecovery(true, _ => { calls++; return pending.Task; }); dialog.ConfirmKeep = () => true;
        Finish(Invoke(dialog, "RefreshAsync")); var first = Invoke(dialog, "KeepAsync");
        Finish(Invoke(dialog, "KeepAsync")); Assert.Equal(1, calls); Assert.False(Field<Button>(dialog, "_keep").Enabled);
        pending.SetException(new IOException("private failure path and exception")); Finish(first);
        Assert.Null(Field<RecoveryReview?>(dialog, "_review")); Assert.False(Field<Button>(dialog, "_keep").Enabled);
        Assert.DoesNotContain("private failure", Field<Label>(dialog, "_status").Text);
        Assert.Contains("private failure", Field<TextBox>(dialog, "_preview").Text);
        Finish(Invoke(dialog, "KeepAsync")); Assert.Equal(1, calls);
    });

    [Fact]
    public void OpenFailureIsInlineAndChangingLanguagePreservesReviewedRevisionAndSelectedCopy() => Sta(() =>
    {
        using var dialog = NewRecovery(true, _ => Task.CompletedTask, _ => throw new IOException("private open error"));
        Finish(Invoke(dialog, "RefreshAsync")); var review = Field<RecoveryReview>(dialog, "_review");
        var paths = Field<ListBox>(dialog, "_paths"); paths.SelectedIndex = 1;
        typeof(ConfigurationRecoveryDialog).GetMethod("OpenSelected", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null);
        Assert.DoesNotContain("private open error", Field<Label>(dialog, "_status").Text);
        Assert.Contains("private open error", Field<TextBox>(dialog, "_preview").Text);
        dialog.SetLanguage(AppLanguage.Spanish);
        Assert.Equal(1, paths.SelectedIndex); Assert.Same(review, Field<RecoveryReview>(dialog, "_review"));
        Assert.Equal("Copia conservada 1", paths.Items[1]!.ToString());
        Assert.Equal("Conservar", Field<Button>(dialog, "_keep").Text);
        Assert.Contains("No se ha podido abrir", Field<Label>(dialog, "_status").Text);
    });

    [Fact]
    public void DisposalDuringReviewDoesNotApplyItsLateResult() => Sta(() =>
    {
        var pending = new TaskCompletionSource<RecoveryReview>();
        var dialog = new ConfigurationRecoveryDialog(new("current.toml", [], () => pending.Task, _ => Task.CompletedTask));
        var refresh = Invoke(dialog, "RefreshAsync"); dialog.Dispose();
        pending.SetResult(new("late content", "late revision", true)); Finish(refresh);
        Assert.Null(Field<RecoveryReview?>(dialog, "_review"));
    });

    [Theory]
    [InlineData(200)]
    [InlineData(300)]
    public void HighDpiExpansionUsesScrollableBodyWithoutGrowingTheWindowMinimum(int percent) => Sta(() =>
    {
        using var dialog = NewRecovery(true, _ => Task.CompletedTask);
        dialog.AutoScaleMode = AutoScaleMode.None;
        dialog.Scale(new System.Drawing.SizeF(percent / 100f, percent / 100f));
        var before = dialog.MinimumSize;
        var button = Field<LinkLabel>(dialog, "_technical");
        typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [new LinkLabelLinkClickedEventArgs(button.Links[0])]);
        Assert.Equal(before, dialog.MinimumSize);
        Assert.True(Field<bool>(dialog, "_expanded"));
        var body = Field<Panel>(dialog, "_bodyScroll"); var footer = Field<FlowLayoutPanel>(dialog, "_footer");
        Assert.True(body.AutoScroll);
        Assert.Same(body.Parent, footer.Parent);
        Assert.DoesNotContain(footer, body.Controls.Cast<Control>());
        Assert.Equal(DockStyle.Fill, footer.Dock);
        var fitted = DialogStyle.FitWithin(dialog.MinimumSize, new System.Drawing.Size(1888, 1040));
        Assert.InRange(fitted.Width, 1, 1888); Assert.InRange(fitted.Height, 1, 1040);
    });

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public void RecoveryFooterReservesItsButtonsAfterContentIsDetachedAndInheritsAFont(int percent) => Sta(() =>
    {
        using var dialog = NewRecovery(true, _ => Task.CompletedTask, _ => { });
        dialog.AutoScaleMode = AutoScaleMode.None;
        if (percent != 100) dialog.Scale(new System.Drawing.SizeF(percent / 100f, percent / 100f));
        dialog.PerformLayout();
        var content=dialog.Controls[0]; content.Font=dialog.Font; dialog.Controls.Remove(content);
        using(content)
        {
            content.Visible=true;content.Dock=DockStyle.None;content.Size=dialog.ClientSize;content.CreateControl();content.PerformLayout();
            var footer=Field<FlowLayoutPanel>(dialog,"_footer");footer.PerformLayout();
            foreach(Control action in footer.Controls)
                Assert.True(action.Top>=0&&action.Bottom<=footer.ClientSize.Height,$"Detached footer clips {action.Text}: {action.Bounds} vs {footer.ClientSize}");
            Assert.True(footer.Height>=footer.Controls.Cast<Control>().Max(action=>action.GetPreferredSize(System.Drawing.Size.Empty).Height));
        }
    });

    [Theory]
    [InlineData(1950, 1080, 1920, 1040, 1920, 1040)]
    [InlineData(1360, 1280, 1920, 1040, 1360, 1040)]
    [InlineData(650, 360, 800, 600, 650, 360)]
    [InlineData(0, -10, 1920, 1080, 1, 1)]
    public void SharedWindowSizerClampsBothDimensionsToTheAvailableWorkArea(int width, int height, int availableWidth, int availableHeight, int expectedWidth, int expectedHeight)
        => Assert.Equal(new System.Drawing.Size(expectedWidth, expectedHeight), DialogStyle.FitWithin(new(width, height), new(availableWidth, availableHeight)));

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    public void SharedWindowSizerRejectsAnInvalidWorkArea(int width, int height)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DialogStyle.FitWithin(new(650, 360), new(width, height)));

    [Fact]
    public void LateLanguageChangePreservesFailureFeedbackAndKeepsRawEvidenceCollapsed() => Sta(() =>
    {
        using var dialog = new ConfigurationRecoveryDialog(new("current.toml", [], () => Task.FromException<RecoveryReview>(new IOException("private diagnostic path")), _ => Task.CompletedTask));
        Finish(Invoke(dialog, "RefreshAsync"));
        Assert.Contains("could not be checked", Field<Label>(dialog, "_status").Text);
        dialog.SetLanguage(AppLanguage.Spanish);
        Assert.Contains("No se ha podido comprobar", Field<Label>(dialog, "_status").Text);
        Assert.DoesNotContain("private diagnostic", Field<Label>(dialog, "_status").Text);
        Assert.Contains("private diagnostic", Field<TextBox>(dialog, "_preview").Text);
        Assert.False(Field<bool>(dialog, "_expanded"));
        Assert.False(Field<Button>(dialog, "_keep").Enabled);
        dialog.SetLanguage(AppLanguage.English);
        Assert.Contains("could not be checked", Field<Label>(dialog, "_status").Text);
    });

    [Theory]
    [InlineData("Tema")]
    [InlineData("theme")]
    public void ThemeFailuresUseAHumanThemeGroup(string area) => Sta(() =>
    {
        using var dialog = new DiagnosticListDialog(_ => { });
        dialog.SetDiagnostics([new(area, "ThemeApplyFailed", "technical summary", "technical evidence")]);
        var list = Field<DataGridView>(dialog, "_items"); Assert.Equal("Theme", list.Rows[0].Cells[0].Value);
        dialog.SetLanguage(AppLanguage.Spanish); Assert.Equal("Tema", list.Rows[0].Cells[0].Value);
    });
    [Theory]
    [InlineData(AppLanguage.English, 100)]
    [InlineData(AppLanguage.Spanish, 100)]
    [InlineData(AppLanguage.English, 200)]
    [InlineData(AppLanguage.Spanish, 300)]
    public void RecoveryFooterHasOnlyTwoShortButtonsOnOneRowWithTextThatFits(AppLanguage language, int percent) => Sta(() =>
    {
        using var dialog = NewRecovery(true, _ => Task.CompletedTask, _ => { });
        dialog.SetLanguage(language); dialog.AutoScaleMode = AutoScaleMode.None;
        dialog.Scale(new System.Drawing.SizeF(percent / 100f, percent / 100f));
        var available = new System.Drawing.Size(1888, 1040);
        dialog.MinimumSize = DialogStyle.FitWithin(dialog.MinimumSize, available);
        dialog.Size = DialogStyle.FitWithin(dialog.Size, available);
        dialog.PerformLayout();
        var footer = Field<FlowLayoutPanel>(dialog, "_footer"); footer.PerformLayout();
        var keep = Field<Button>(dialog, "_keep"); var close = Field<Button>(dialog, "_close");
        Assert.Equal(2, footer.Controls.Count); Assert.False(footer.WrapContents);
        Assert.Equal(language == AppLanguage.Spanish ? "Conservar" : "Keep", keep.Text);
        Assert.Equal(language == AppLanguage.Spanish ? "Cerrar" : "Close", close.Text);
        Assert.Equal(keep.Top, close.Top);
        Assert.True(keep.Right + 8 <= close.Left, "Footer buttons need a visible gap.");
        foreach (var action in new[] { keep, close })
        {
            var text = TextRenderer.MeasureText(action.Text, action.Font, System.Drawing.Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            Assert.True(action.ClientSize.Width >= text.Width + action.Padding.Horizontal, "The action label is truncated: " + action.Text);
            Assert.True(action.Right <= footer.ClientSize.Width && action.Bottom <= footer.ClientSize.Height, "Footer action is outside its row.");
        }
        var open = Field<Button>(dialog, "_open"); var check = Field<Button>(dialog, "_refresh");
        Assert.Equal(language == AppLanguage.Spanish ? "Abrir" : "Open", open.Text);
        Assert.Equal(language == AppLanguage.Spanish ? "Comprobar" : "Check", check.Text);
        Assert.Same(Field<ListBox>(dialog, "_paths").Parent, open.Parent);
        Assert.Same(Field<Label>(dialog, "_status").Parent, check.Parent);
        Assert.DoesNotContain(open, footer.Controls.Cast<Control>()); Assert.DoesNotContain(check, footer.Controls.Cast<Control>());
        Assert.IsType<LinkLabel>(Field<LinkLabel>(dialog, "_technical"));
        Assert.Equal(language == AppLanguage.Spanish ? "Revisar configuración" : "Review configuration", Field<Label>(dialog, "_heading").Text);
    });

    [Fact]
    public void OpeningReviewStartsValidationWithoutRequiringACheckButtonClick() => Sta(() =>
    {
        int reviews = 0;
        using var dialog = new ConfigurationRecoveryDialog(new("current.toml", [], () => { reviews++; return Task.FromResult(new RecoveryReview("current", "revision-A", true)); }, _ => Task.CompletedTask));
        Assert.False(Field<Button>(dialog, "_keep").Enabled);
        Finish(dialog.ReviewOnOpenAsync());
        Assert.Equal(1, reviews); Assert.True(Field<Button>(dialog, "_keep").Enabled);
    });
    [Fact]
    public void IssueGridOwnsItsHeaderAndRowColorsAndWrapsPendingStatusInsteadOfUsingNativeGroups() => Sta(() =>
    {
        using var dialog = new DiagnosticListDialog(_ => { }, AppLanguage.Spanish);
        dialog.SetDiagnostics([new("Configuración", "InvalidToml", "technical summary", "technical details")]);
        var grid = Field<DataGridView>(dialog, "_items");
        Assert.True(grid.ReadOnly); Assert.False(grid.RowHeadersVisible); Assert.False(grid.EnableHeadersVisualStyles);
        Assert.Equal(DataGridViewSelectionMode.FullRowSelect, grid.SelectionMode);
        Assert.Equal(DataGridViewAutoSizeColumnsMode.Fill, grid.AutoSizeColumnsMode);
        Assert.Equal(DataGridViewAutoSizeRowsMode.AllCells, grid.AutoSizeRowsMode);
        Assert.Equal(DataGridViewTriState.True, grid.DefaultCellStyle.WrapMode);
        Assert.Equal(new[] { "Área", "Incidencia", "Estado", "Última vez", "Veces" }, grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText));
        Assert.Equal("Pendiente", grid.Rows[0].Cells[2].Value);
        Assert.True(Contrast(grid.ColumnHeadersDefaultCellStyle.ForeColor, grid.ColumnHeadersDefaultCellStyle.BackColor) >= 4.5, "Header text needs readable contrast.");
        Assert.True(Contrast(grid.DefaultCellStyle.ForeColor, grid.DefaultCellStyle.BackColor) >= 4.5, "Row text needs readable contrast.");
        Assert.True(Contrast(grid.DefaultCellStyle.SelectionForeColor, grid.DefaultCellStyle.SelectionBackColor) >= 4.5, "Selected rows need readable contrast.");
        dialog.PerformLayout(); _ = grid.Handle; grid.PerformLayout(); grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
        Assert.All(grid.Columns.Cast<DataGridViewColumn>(), column => Assert.True(column.Width >= column.MinimumWidth));
        Assert.True(grid.Rows[0].Height >= grid.Rows[0].GetPreferredHeight(0, DataGridViewAutoSizeRowMode.AllCells, true));
    });

    [Fact]
    public void InvalidatedRecoveryWaitsForItsReviewThenClosesWithoutAcceptingTheLateRevision() => Sta(() =>
    {
        int kept = 0; var pending = new TaskCompletionSource<RecoveryReview>();
        using var dialog = new ConfigurationRecoveryDialog(new("current.toml", [], () => pending.Task, _ => { kept++; return Task.CompletedTask; }));
        dialog.ConfirmKeep = () => throw new InvalidOperationException("Obsolete context must never ask for confirmation.");
        var review = dialog.ReviewOnOpenAsync(); dialog.InvalidateContext();
        Assert.False(dialog.IsDisposed); Assert.False(Field<Button>(dialog, "_keep").Enabled);
        Assert.False(Field<Button>(dialog, "_refresh").Enabled);
        Finish(Invoke(dialog, "KeepAsync")); Assert.Equal(0, kept);
        pending.SetResult(new("obsolete content", "obsolete revision", true)); Finish(review);
        Assert.Null(Field<RecoveryReview?>(dialog, "_review")); Assert.True(dialog.IsDisposed); Assert.Equal(0, kept);
    });

    [Fact]
    public void InvalidatingAnIdleRecoveryImmediatelyClosesItAndClearsItsReviewedRevision() => Sta(() =>
    {
        using var dialog = NewRecovery(true, _ => throw new InvalidOperationException("An obsolete review must not be kept."));
        Finish(dialog.ReviewOnOpenAsync()); Assert.NotNull(Field<RecoveryReview?>(dialog, "_review"));
        dialog.InvalidateContext(); Assert.True(dialog.IsDisposed); Assert.Null(Field<RecoveryReview?>(dialog, "_review"));
    });

    [Fact]
    public void InvalidatingAPendingKeepDoesNotInvokeTheObsoleteResolvedCallback() => Sta(() =>
    {
        int kept = 0, resolved = 0; var pending = new TaskCompletionSource();
        using var dialog = new ConfigurationRecoveryDialog(new("current.toml", [], () => Task.FromResult(new RecoveryReview("current", "r1", true)), _ => { kept++; return pending.Task; }, () => resolved++));
        dialog.ConfirmKeep = () => true; Finish(dialog.ReviewOnOpenAsync());
        var keeping = Invoke(dialog, "KeepAsync"); dialog.InvalidateContext();
        Assert.Equal(1, kept); Assert.False(dialog.IsDisposed); Assert.Null(Field<RecoveryReview?>(dialog, "_review"));
        pending.SetResult(); Finish(keeping);
        Assert.Equal(0, resolved); Assert.True(dialog.IsDisposed);
    });

    [Theory]
    [InlineData(false, AppLanguage.English, -256)]
    [InlineData(false, AppLanguage.Spanish, -256)]
    [InlineData(true, AppLanguage.English, -256)]
    [InlineData(true, AppLanguage.Spanish, -256)]
    [InlineData(false, AppLanguage.English, -16777216)]
    [InlineData(false, AppLanguage.Spanish, -16777216)]
    [InlineData(true, AppLanguage.English, -16777216)]
    [InlineData(true, AppLanguage.Spanish, -16777216)]
    public void DialogPaletteRemainsReadableWithAnUnsuitableUserAccent(bool dark, AppLanguage language, int accent)
    {
        var colors = DialogStyle.CreatePalette(dark, false, System.Drawing.Color.FromArgb(accent));
        Assert.False(colors.HighContrast);
        Assert.True(DialogStyle.Contrast(colors.Text, colors.Raised) >= 4.5);
        Assert.True(DialogStyle.Contrast(colors.Muted, colors.Surface) >= 4.5);
        Assert.True(DialogStyle.Contrast(colors.Accent, colors.Surface) >= 4.5);
        Assert.True(DialogStyle.Contrast(colors.AccentText, colors.Accent) >= 4.5);
        Assert.Equal(language == AppLanguage.Spanish ? "Pendiente" : "Pending", DiagnosticText.T(language, "Pending", "Pendiente"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HighContrastPalettePreservesExactSystemColorsWithoutChangingSystemSettings(bool dark)
    {
        var colors = DialogStyle.CreatePalette(dark, true, System.Drawing.Color.Yellow);
        Assert.True(colors.HighContrast);
        Assert.Equal(System.Drawing.SystemColors.Window, colors.Surface);
        Assert.Equal(System.Drawing.SystemColors.Window, colors.Raised);
        Assert.Equal(System.Drawing.SystemColors.WindowText, colors.Text);
        Assert.Equal(System.Drawing.SystemColors.WindowText, colors.Muted);
        Assert.Equal(System.Drawing.SystemColors.WindowFrame, colors.Border);
        Assert.Equal(System.Drawing.SystemColors.Highlight, colors.Accent);
        Assert.Equal(System.Drawing.SystemColors.HighlightText, colors.AccentText);
    }
    [Theory]
    [InlineData(AppLanguage.English, 100)]
    [InlineData(AppLanguage.Spanish, 100)]
    [InlineData(AppLanguage.English, 200)]
    [InlineData(AppLanguage.Spanish, 200)]
    [InlineData(AppLanguage.English, 300)]
    [InlineData(AppLanguage.Spanish, 300)]
    public void IssueHeadersFitAtTheMinimumWindowWidthWithoutHorizontalOverflow(AppLanguage language, int percent) => Sta(() =>
    {
        float scale = percent / 100f;
        using var font = new System.Drawing.Font("Segoe UI", 10.5f * scale);
        using var dialog = new DiagnosticListDialog(_ => { });
        dialog.AutoScaleMode = AutoScaleMode.None; dialog.Font = font;
        var area = new System.Drawing.Size(1888, 1040);
        dialog.MinimumSize = DialogStyle.FitWithin(new((int)(650 * scale), (int)(360 * scale)), area);
        dialog.Size = dialog.MinimumSize;
        dialog.SetLanguage(language);
        dialog.SetDiagnostics([new("Configuración", "InvalidToml", "summary", "details")]);
        dialog.PerformLayout(); var grid = Field<DataGridView>(dialog, "_items"); _ = grid.Handle; grid.PerformLayout();
        foreach (DataGridViewColumn column in grid.Columns)
        {
            Assert.Equal(DataGridViewColumnSortMode.NotSortable, column.SortMode);
            var text = TextRenderer.MeasureText(column.HeaderText, grid.Font, System.Drawing.Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            int padding = (int)Math.Ceiling(24 * grid.DeviceDpi / 96.0);
            Assert.True(column.MinimumWidth >= text.Width + padding, $"Header minimum is too small for '{column.HeaderText}': {column.MinimumWidth} < {text.Width + padding}");
            Assert.True(column.Width >= column.MinimumWidth, "A header was squeezed below its minimum width.");
        }
        Assert.True(grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) <= grid.ClientSize.Width, "The minimum-sized issue table needs horizontal scrolling.");
        Assert.Equal(language == AppLanguage.Spanish ? "Revisar" : "Review", Field<Button>(dialog, "_details").Text);
    });
    [Theory]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.Spanish)]
    public void AreaColumnUsesSpareWidthForItsContentWithoutIncreasingItsMinimum(AppLanguage language) => Sta(() =>
    {
        using var dialog = new DiagnosticListDialog(_ => { }, language);
        dialog.PerformLayout(); var grid = Field<DataGridView>(dialog, "_items"); _ = grid.Handle;
        int minimum = grid.Columns["Area"]!.MinimumWidth;
        dialog.SetDiagnostics([new("Configuración", "InvalidToml", "summary", "details")]);
        dialog.PerformLayout(); grid.PerformLayout();
        string label = grid.Rows[0].Cells["Area"].Value!.ToString()!;
        int textWidth = TextRenderer.MeasureText(label, grid.Font).Width + grid.DefaultCellStyle.Padding.Horizontal;
        Assert.Equal(minimum, grid.Columns["Area"]!.MinimumWidth);
        Assert.True(grid.Columns["Area"]!.Width >= textWidth, "The default-sized area column clips its category.");
        Assert.True(grid.Columns["Issue"]!.Width >= grid.Columns["Issue"]!.MinimumWidth);
        Assert.True(grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) <= grid.ClientSize.Width);
    });
    private static double Contrast(System.Drawing.Color foreground, System.Drawing.Color background)
    {
        Assert.False(foreground.IsEmpty); Assert.False(background.IsEmpty);
        static double Luminance(System.Drawing.Color color)
        {
            static double Linear(byte value) { double s = value / 255.0; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double a = Luminance(foreground), b = Luminance(background);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static ConfigurationRecoveryDialog NewRecovery(bool valid, Func<string, Task> keep, Action<string>? open = null)
        => new(new(@"D:\private\current.toml", [@"D:\private\preserved.toml"], () => Task.FromResult(new RecoveryReview("current content", "revision-A", valid, valid ? null : "private validation details")), keep), open);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static Task Invoke(ConfigurationRecoveryDialog value, string name) => (Task)typeof(ConfigurationRecoveryDialog).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
    private static void Finish(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted) { if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Private UI task did not finish."); Application.DoEvents(); Thread.Yield(); }
        task.GetAwaiter().GetResult();
    }
    private static void Sta(Action body)
    {
        Exception? failure = null; var thread = new Thread(() => { try { body(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Private UI fixture timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
