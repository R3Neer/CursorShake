using System.IO.MemoryMappedFiles;
using System.Diagnostics;
using System.Text.Json;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;

namespace CursorShake.NativeSmoke;

internal static class GuardReadyCapture
{
    internal static int Run(string stateDirectory, string guardExecutable, string reportPath)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        return Task.Run(() => RunCore(stateDirectory, guardExecutable, reportPath)).GetAwaiter().GetResult();
    }
    private static int RunCore(string stateDirectory, string guardExecutable, string reportPath)
    {
        bool ready = false, ledgerRemains = false;
        string? failure = null, ledgerName = null;
        var guardProcesses = new List<(int Id, long StartTicks)>();
        var remainingGuardPids = new List<int>();
        try
        {
            using (var session = new NativeCursorSession(stateDirectory, guardExecutable, startGuard: true, checkpoint: phase => { if (phase == RecoveryCheckpoint.GuardReady) ready = true; }))
            {
                if (!ready) throw new InvalidOperationException("RecoveryGuard did not signal readiness.");
                foreach (var process in Process.GetProcessesByName("CursorShake.RecoveryGuard"))
                {
                    using (process)
                    {
                        if (string.Equals(process.MainModule?.FileName, guardExecutable, StringComparison.OrdinalIgnoreCase))
                            guardProcesses.Add((process.Id, process.StartTime.ToUniversalTime().Ticks));
                    }
                }
                if (guardProcesses.Count == 0) throw new InvalidOperationException("Cannot identify the ready RecoveryGuard process.");
                string directory = Directory.EnumerateDirectories(Path.Combine(stateDirectory, "recovery")).Single();
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "session.json")));
                var root = document.RootElement;
                ledgerName = $"Local\\CursorShake.Recovery.{root.GetProperty("OwnerSid").GetString()}.{root.GetProperty("SessionId").GetInt32()}.{root.GetProperty("Nonce").GetString()}";
                if (!RecoveryBootstrap.Inspect(stateDirectory).Single().LedgerAvailable) throw new InvalidOperationException("Ready guard did not retain ownership evidence.");
            }
            if (ledgerName is not null)
            {
                try { using var map = MemoryMappedFile.OpenExisting(ledgerName); ledgerRemains = true; }
                catch (FileNotFoundException) { }
            }
            foreach (var guard in guardProcesses)
            {
                try
                {
                    using var process = Process.GetProcessById(guard.Id);
                    if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == guard.StartTicks) remainingGuardPids.Add(guard.Id);
                }
                catch (ArgumentException) { }
            }
            if (remainingGuardPids.Count != 0 || ledgerRemains || RecoveryBootstrap.Inspect(stateDirectory).Count != 0) throw new InvalidOperationException("Guard teardown left a process, recovery handles or receipts.");
        }
        catch (Exception exception) { failure = exception.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Passed = failure is null, MutationAttempted = false, GuardReady = ready, GuardPids = guardProcesses.Select(p => p.Id).ToArray(), RemainingGuardPids = remainingGuardPids, LedgerRemains = ledgerRemains, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(reportPath);
        if (failure is not null) { Console.Error.WriteLine(failure); return 1; }
        return 0;
    }
}
