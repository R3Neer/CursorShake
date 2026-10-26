using System.Diagnostics;
using System.Text.Json;
using CursorShake.Windows.Lifecycle;

namespace CursorShake.NativeSmoke;

internal static class MaintenanceProcessSmoke
{
    internal static int Child(int exitCode, string ready)
    {
        using var stop = new ManualResetEvent(false);
        using var endpoint = new MaintenanceEndpoint(() => stop.Set());
        using var process = Process.GetCurrentProcess();
        File.WriteAllText(ready + ".tmp", JsonSerializer.Serialize(new { process.Id, StartTicks = process.StartTime.ToUniversalTime().Ticks }));
        File.Move(ready + ".tmp", ready);
        return stop.WaitOne(TimeSpan.FromSeconds(15)) ? exitCode : 91;
    }

    internal static int Run(string report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var cases = new List<object>(); string? failure = null;
        try { Execute().GetAwaiter().GetResult(); } catch (Exception error) { failure = error.ToString(); }
        File.WriteAllText(report, JsonSerializer.Serialize(new { Passed = failure is null, Runtime = Environment.Version.ToString(), OnlyOwnFixtureProcesses = true, RealApplicationQuiesced = false, AppHostCreated = false, SchedulerModified = false, SystemCursorMutation = false, Cases = cases, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(report); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;

        async Task Execute()
        {
            foreach (int expectedExit in new[] { 0, 1 })
            {
                string ready = Path.Combine(Path.GetDirectoryName(report)!, "maintenance-child-" + Guid.NewGuid().ToString("N") + ".json");
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add("--maintenance-process-child"); start.ArgumentList.Add(expectedExit.ToString(System.Globalization.CultureInfo.InvariantCulture)); start.ArgumentList.Add(ready);
                using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start own maintenance fixture.");
                try
                {
                    using var watcher = new FileSystemWatcher(Path.GetDirectoryName(ready)!) { EnableRaisingEvents = true };
                    var wait = Stopwatch.StartNew();
                    while (!File.Exists(ready))
                    {
                        if (child.HasExited) throw new InvalidOperationException("Fixture exited before creating its endpoint.");
                        if (wait.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Fixture endpoint was not ready.");
                        watcher.WaitForChanged(WatcherChangeTypes.All, 100);
                    }
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    Exception? clientError = null;
                    try { await MaintenanceEndpoint.RequestShutdownAndWaitAsync(child, timeout.Token); }
                    catch (Exception error) { clientError = error; }
                    if (!child.HasExited || child.ExitCode != expectedExit) throw new InvalidOperationException("Fixture did not reach the requested semantic exit code.");
                    if (expectedExit == 0 && clientError is not null) throw new InvalidOperationException("Clean fixture exit was rejected.", clientError);
                    if (expectedExit != 0 && (clientError is not IOException || !clientError.Message.Contains("code 1", StringComparison.Ordinal))) throw new InvalidOperationException("Nonzero fixture exit was not reported by the public maintenance client.", clientError);
                    cases.Add(new { Pid = child.Id, ExpectedExitCode = expectedExit, ActualExitCode = child.ExitCode, PublicClientAccepted = clientError is null, Error = clientError?.Message, ProcessExited = child.HasExited });
                }
                finally
                {
                    if (!child.HasExited) { child.Kill(entireProcessTree: false); await child.WaitForExitAsync(); }
                }
            }
        }
    }
}
