using System.Collections.Concurrent;
using System.Text.Json;
using CursorShake.App;
using CursorShake.App.Dialogs;
using CursorShake.Windows.Recovery;

namespace CursorShake.NativeSmoke;

internal static class AppSmoke
{
    internal static int Run(string root, string bundledRoot, string guardExecutable, string reportPath)
    {
        Console.Error.WriteLine("EXCLUSIVE WINDOWS QA: this mode starts the real application and temporarily replaces system cursors.");
        using var owner = new StandardPointerOwner.Client(root);
        var diagnostics = new ConcurrentQueue<UiDiagnostic>();
        string? failure = null;
        bool ready = false, previewAccepted = false, paused = false, stopped = false, ownerStopped = false;
        var ui = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using var host = new AppHost(new AppHostOptions
                {
                    WorkspaceRoot = Path.Combine(root, "workspace"), StateDirectory = Path.Combine(root, "state"),
                    BundledThemes = bundledRoot, GuardExecutable = guardExecutable, PersistLocator = false,
                    DiagnosticPresenter = diagnostics.Enqueue
                });
                EventHandler? firstIdle = null;
                firstIdle = async (_, _) =>
                {
                    Application.Idle -= firstIdle;
                    try
                    {
                        await host.Ready.WaitAsync(TimeSpan.FromSeconds(25));
                        ready = true;
                        if (host.CurrentDiagnostics.Count != 0) throw new InvalidOperationException("Application startup reported diagnostics.");
                        await host.PreviewAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        previewAccepted = true;
                        // Observe a nonzero part of the configured animation before issuing cancellation.
                        await Task.Delay(300);
                        await host.PauseAsync(true).WaitAsync(TimeSpan.FromSeconds(5));
                        paused = true;
                    }
                    catch (Exception exception) { failure = exception.ToString(); }
                    finally
                    {
                        try { await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(25)); await host.Stopped; stopped = true; }
                        catch (Exception exception) { failure = (failure is null ? "" : failure + Environment.NewLine) + exception; }
                    }
                };
                Application.Idle += firstIdle;
                Application.Run(host);
            }
            catch (Exception exception) { failure = exception.ToString(); }
        }) { IsBackground = true, Name = "CursorShake app smoke UI" };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        if (!ui.Join(TimeSpan.FromSeconds(60))) failure = "Application smoke UI did not stop within60seconds; inspect recovery state.";
        try { owner.Dispose(); ownerStopped = true; }
        catch (Exception exception) { failure = (failure is null ? "" : failure + Environment.NewLine) + exception; }
        if (failure is null && RecoveryBootstrap.Inspect(Path.Combine(root, "state")).Any(p => p.BlocksNewSession)) failure = "Application smoke left blocking recovery receipts.";
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Passed = failure is null && ready && previewAccepted && paused && stopped && diagnostics.IsEmpty, SystemCursorMutationAttempted = true, PointerOwnerPid = owner.Pid, PointerOwnerReadyPath = owner.ReadyPath, PointerOwnerStopped = ownerStopped, Ready = ready, PreviewAccepted = previewAccepted, Paused = paused, Stopped = stopped, Failure = failure, Diagnostics = diagnostics.ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(reportPath);
        return failure is null && ready && previewAccepted && paused && stopped && diagnostics.IsEmpty ? 0 : 1;
    }
}
