using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using CursorShake.App.Dialogs;

namespace CursorShake.Windows.Tests;

public sealed class GuiRecoveryWorkflowTests
{
    [Fact] public void CancelledConfirmationDoesNotRunAction() => Sta(() =>
    {
        int calls = 0; var action = new DiagnosticAction("Restablecer…", "Confirmar", () => { calls++; return Task.CompletedTask; }); using var dialog = new DiagnosticDialog(new("Inicio", "RecoveryRequired", "Recuperación pendiente", "Detalles"),
            additionalAction: action);
        SetConfirm(dialog, false); RunAction(dialog, action).GetAwaiter().GetResult(); Assert.Equal(0, calls);
    });
    [Fact] public void ConfirmedActionRunsOnceAndSuppressesReentryWhilePending() => Sta(() =>
    {
        int calls = 0; var pending = new TaskCompletionSource();
        var action = new DiagnosticAction("Restablecer…", "Confirmar", () => { calls++; return pending.Task; });
        using var dialog = new DiagnosticDialog(new("Inicio", "RecoveryRequired", "Recuperación pendiente", "Detalles"),
            additionalAction: action);
        SetConfirm(dialog, true); Task first = RunAction(dialog, action); RunAction(dialog, action).GetAwaiter().GetResult();
        Assert.False(first.IsCompleted); Assert.Equal(1, calls); pending.SetResult(); first.GetAwaiter().GetResult();
    });
    [Fact] public void PendingOperationDelaysShutdownAndNewOperationsAreRefused() => Sta(() =>
    {
        using var center = new DiagnosticCenter(presenter: _ => { }); var pending = new TaskCompletionSource();
        Task running = Track(center, () => pending.Task); Assert.False(center.PendingOperations.IsCompleted);
        Task stopping = center.BeginShutdownAsync(); Assert.False(stopping.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => Track(center, () => Task.CompletedTask).GetAwaiter().GetResult());
        pending.SetResult(); running.GetAwaiter().GetResult(); stopping.GetAwaiter().GetResult(); Assert.True(center.PendingOperations.IsCompleted);
    });
    [Fact] public void FailedOperationStillCompletesShutdownBarrier() => Sta(() =>
    {
        using var center = new DiagnosticCenter(presenter: _ => { }); var pending = new TaskCompletionSource();
        Task running = Track(center, () => pending.Task); Task stopping = center.BeginShutdownAsync();
        pending.SetException(new IOException("Expected failure")); Assert.Throws<IOException>(() => running.GetAwaiter().GetResult());
        stopping.GetAwaiter().GetResult(); Assert.True(center.PendingOperations.IsCompleted);
    });
    [Theory] [InlineData(false)] [InlineData(true)]
    public void RecoveryReviewControlsWhetherTheCurrentRevisionMayBeKept(bool valid) => Sta(() =>
    {
        int reviewed = 0, kept = 0; var expected = new RecoveryReview("# current", "revision-A", valid, valid ? null : "Invalid theme");
        using var dialog = new ConfigurationRecoveryDialog(new("config.toml", ["before.toml", "candidate.toml"],
            () => { reviewed++; return Task.FromResult(expected); }, revision => { kept++; Assert.Equal("revision-A", revision); return Task.CompletedTask; }));
        ((Task)typeof(ConfigurationRecoveryDialog).GetMethod("RefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!).GetAwaiter().GetResult();
        var review = (RecoveryReview)typeof(ConfigurationRecoveryDialog).GetField("_review", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        var keep = (Button)typeof(ConfigurationRecoveryDialog).GetField("_keep", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        Assert.Same(expected, review); Assert.Equal(valid, keep.Enabled); Assert.Equal(1, reviewed); Assert.Equal(0, kept);
    });
    [Fact] public void DiagnosticAndRecoveryFormsRenderPrivatelyAtTwoLogicalScales() => Sta(() =>
    {
        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "tests", "Fixtures", "Calibration"))) root = Directory.GetParent(root)!.FullName;
        string output = Path.Combine(root, "artifacts", "tests", "gui"); Directory.CreateDirectory(output);
        foreach (int logicalDpi in new[] { 96, 192 })
        {
            using var diagnostic = new DiagnosticDialog(new("Configuración", "InvalidToml", "No se ha podido aplicar la configuración", "La clave appearance.theme hace referencia a un tema que no existe.\r\nSe conserva la configuración anterior hasta que corrijas el archivo.", @"D:\Datos\CursorShake\config.toml", 12, 3),
                _ => { }, () => { }, additionalAction: new("Volver a cargar esquema…", "Confirmar", () => Task.CompletedTask));
            using var recovery = new ConfigurationRecoveryDialog(new(@"D:\Datos\CursorShake\config.toml", [@"D:\Datos\CursorShake\before.toml", @"D:\Datos\CursorShake\candidate.toml"], () => Task.FromResult(new RecoveryReview("# Configuración actual\r\n[appearance]\r\ntheme = \"windows\"", "r1", true)), _ => Task.CompletedTask), _ => { });
            ((Task)typeof(ConfigurationRecoveryDialog).GetMethod("RefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recovery, null)!).GetAwaiter().GetResult();
            Render(diagnostic, Path.Combine(output, $"diagnostic-logical-{logicalDpi}.png"), logicalDpi);
            Render(recovery, Path.Combine(output, $"recovery-logical-{logicalDpi}.png"), logicalDpi);
        }
        File.WriteAllText(Path.Combine(output, "render-notes.txt"), "Private detached actual form-layout DrawToBitmap renders; forms were never shown. 96 is base logical layout; 192 uses explicit 2x Control.Scale, not a real monitor-DPI transition or desktop screenshot.");
    });
    private static void Render(Form form, string path, int logicalDpi)
    {
        form.AutoScaleMode = AutoScaleMode.None;
        if (logicalDpi != 96) form.Scale(new System.Drawing.SizeF(logicalDpi / 96f, logicalDpi / 96f));
        // Render the form's actual layout detached from its never-visible top-level window.
        // Hidden top-level forms otherwise omit their invisible descendants in DrawToBitmap.
        form.PerformLayout();
        foreach (var control in Children(form))
        {
            if (control.Parent is null) continue;
            Assert.True(control.Left >= 0 && control.Top >= 0 && control.Right <= control.Parent.ClientSize.Width && control.Bottom <= control.Parent.ClientSize.Height,
                $"{control.GetType().Name} '{control.Text}' overflows parent at logical DPI {logicalDpi}: {control.Bounds} vs {control.Parent.ClientSize}");
        }
        File.WriteAllLines(path + ".bounds.txt", Children(form).Select(c => $"{c.GetType().Name}: {c.Text.Replace("\r", " ").Replace("\n", " ")} Bounds={c.Bounds} Visible={c.Visible}"));
        var content = form.Controls[0]; form.Controls.Remove(content);
        using (content)
        {
        content.Dock = DockStyle.None; content.Size = form.ClientSize; content.CreateControl(); content.PerformLayout();
        using var bitmap = new System.Drawing.Bitmap(content.Width, content.Height);
        content.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        Assert.True(new FileInfo(path).Length > 2000);
    }
    private static Task Track(DiagnosticCenter center, Func<Task> callback) => (Task)typeof(DiagnosticCenter).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m => m.Name == "Track" && !m.IsGenericMethod).Invoke(center, [callback])!;
    private static void SetConfirm(DiagnosticDialog dialog, bool confirm) => typeof(DiagnosticDialog).GetProperty("ConfirmAction", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, (Func<DiagnosticAction, bool>)(_ => confirm));
    private static Task RunAction(DiagnosticDialog dialog, DiagnosticAction action)
    {
        var button = Children(dialog).OfType<Button>().Single(b => b.Text == "Restablecer…");
        return (Task)typeof(DiagnosticDialog).GetMethod("RunAdditionalAction", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [action, button])!;
    }
    private static IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
    private static void Sta(Action action)
    {
        Exception? error = null; var thread = new Thread(() => { try { action(); } catch(Exception failure) { error = failure; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if(error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
