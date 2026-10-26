using System.Diagnostics;
using System.Text.Json;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;

namespace CursorShake.NativeSmoke;

internal static class CrashGuardSmoke
{
    internal static int Child(string state, string guard, string ready)
    {
        using var session = new NativeCursorSession(state, guard);
        session.Publish(CursorRole.Arrow, Colored(64, 51));
        session.Publish(CursorRole.Text, Colored(64, 197));
        var helper = Process.GetProcessesByName("CursorShake.RecoveryGuard").Single(p => string.Equals(p.MainModule?.FileName, guard, StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(ready + ".tmp", JsonSerializer.Serialize(new { Pid = Environment.ProcessId, GuardPid = helper.Id }));
        File.Move(ready + ".tmp", ready);
        new ManualResetEvent(false).WaitOne(TimeSpan.FromSeconds(90)); // Only the parent terminates this deliberately disposable child.
        return 3;
    }

    internal static int Run(string root, string guard, string output)
    {
        Directory.CreateDirectory(root);
        string ready = Path.Combine(root, "child-ready.json"), state = Path.Combine(root, "state");
        var baseline = CursorRoles.All.ToDictionary(role => role, role => Convert.ToHexString(CursorFingerprint.Current(role)));
        var observations = new List<object>();
        string? failure = null;
        int childPid = 0, guardPid = 0;
        Process? child = null;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in new[] { "--crash-child", state, guard, ready }) start.ArgumentList.Add(argument);
            child = Process.Start(start) ?? throw new InvalidOperationException("Crash child could not start.");
            childPid = child.Id;
            using var watcher = new FileSystemWatcher(root) { EnableRaisingEvents = true };
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (child.HasExited) throw new InvalidOperationException("Crash child exited before publishing its test cursors.");
                if (timeout.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Crash child was not ready.");
                watcher.WaitForChanged(WatcherChangeTypes.All, 200);
            }
            using var data = JsonDocument.Parse(File.ReadAllText(ready));
            guardPid = data.RootElement.GetProperty("GuardPid").GetInt32();
            using var helper = Process.GetProcessById(guardPid);
            foreach (var role in new[] { CursorRole.Arrow, CursorRole.Text })
                if (baseline[role] == Convert.ToHexString(CursorFingerprint.Current(role))) throw new InvalidOperationException("Child did not publish the expected changed role: " + role);
            child.Kill(entireProcessTree: false); // The independent recovery helper must survive its parent.
            if (!child.WaitForExit(5000) || !helper.WaitForExit(15000)) throw new TimeoutException("Killed child or recovery helper did not finish.");
            foreach (var role in CursorRoles.All)
            {
                string after = Convert.ToHexString(CursorFingerprint.Current(role));
                observations.Add(new { Role = role.ToString(), Before = baseline[role], After = after, Equal = baseline[role] == after });
                if (baseline[role] != after) throw new InvalidOperationException("Recovery fingerprint differs for " + role);
            }
            if (RecoveryBootstrap.Inspect(state).Any(p => p.BlocksNewSession)) throw new InvalidOperationException("Crash recovery left blocking receipts.");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            // A failed assertion must also terminate only our disposable parent so its independent helper can compensate.
            if (child is not null)
            {
                try
                {
                    if (!child.HasExited) { child.Kill(entireProcessTree: false); child.WaitForExit(5000); }
                    if (guardPid != 0)
                    {
                        try { using var helper = Process.GetProcessById(guardPid); if (!helper.WaitForExit(15000)) failure = (failure ?? "") + " Recovery helper is still running; preserve its state."; }
                        catch (ArgumentException) { }
                    }
                }
                catch (Exception cleanup) { failure = (failure ?? "") + Environment.NewLine + cleanup; }
                finally { child.Dispose(); }
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = failure is null, MutationAttempted = true, ChildPid = childPid, GuardPid = guardPid, Failure = failure, Cases = observations }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }

    internal static CursorPixels Colored(int size, byte blue)
    {
        byte[] pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = blue; pixels[i + 1] = 83; pixels[i + 3] = 255; }
        return new(size, size, 1, 1, pixels, new byte[((size + 31) / 32) * 4 * size]);
    }
}
