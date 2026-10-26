using System.Diagnostics;
namespace CursorShake.Windows.Lifecycle;

public static class InstallationProcesses
{
    public static async Task QuiesceAsync(string root, CancellationToken cancellationToken = default)
    {
        root = InstallationGate.CanonicalRoot(root); InstallationGate.AssertMaintenanceHeld(root);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); limit.CancelAfter(TimeSpan.FromSeconds(30));
        var held = new List<Candidate>();
        try
        {
            var initial = Find(root); held.AddRange(initial);
            foreach (var app in initial.Where(p => p.Name == "CursorShake"))
                await MaintenanceEndpoint.RequestShutdownAndWaitAsync(app.Process, limit.Token).ConfigureAwait(false);
            var afterApps = Find(root); held.AddRange(afterApps);
            foreach (var guard in afterApps.Where(p => p.Name == "CursorShake.RecoveryGuard"))
            {
                await guard.Process.WaitForExitAsync(limit.Token).ConfigureAwait(false); MaintenanceEndpoint.RequireCleanExit(guard.Process);
            }
            foreach (var guard in initial.Where(p => p.Name == "CursorShake.RecoveryGuard"))
            {
                await guard.Process.WaitForExitAsync(limit.Token).ConfigureAwait(false); MaintenanceEndpoint.RequireCleanExit(guard.Process);
            }
            var remaining = Find(root); held.AddRange(remaining);
            if (remaining.Count != 0) throw new IOException("CursorShake processes are still active in this installation. Exit them from the tray menu before continuing setup.");
            InstallationGate.AssertMaintenanceHeld(root);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        { throw new IOException("CursorShake did not finish its verified shutdown within 30 seconds. No process was terminated; close it from the tray and retry setup.", e); }
        finally { foreach (var candidate in held) candidate.Process.Dispose(); }
    }
    internal static bool MatchesExecutable(string root, string name, string executablePath)
        => name is "CursorShake" or "CursorShake.RecoveryGuard" && string.Equals(Path.GetFullPath(executablePath), Path.Combine(InstallationGate.CanonicalRoot(root), name + ".exe"), StringComparison.OrdinalIgnoreCase);
    private sealed record Candidate(string Name, Process Process);
    private static List<Candidate> Find(string root)
    {
        var matches = new List<Candidate>();
        try
        {
            foreach (string name in new[] { "CursorShake", "CursorShake.RecoveryGuard" })
            foreach (var process in Process.GetProcessesByName(name))
            {
                bool keep = false;
                try
                {
                    if (process.Id == Environment.ProcessId || process.HasExited) continue;
                    _ = process.SafeHandle; _ = process.StartTime;
                    string path = process.MainModule?.FileName ?? throw new IOException("Executable path is unavailable.");
                    if (MatchesExecutable(root, name, path))
                    { matches.Add(new(name, process)); keep = true; }
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
                {
                    if (!process.HasExited) throw new IOException($"Cannot verify the executable identity of {name} process {process.Id}; setup will not close or ignore it.", e);
                }
                finally { if (!keep) process.Dispose(); }
            }
            return matches;
        }
        catch { foreach (var candidate in matches) candidate.Process.Dispose(); throw; }
    }
}
