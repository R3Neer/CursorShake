using System.Diagnostics;
using System.Text.Json;
using CursorShake.Core.Runtime;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;

namespace CursorShake.NativeSmoke;

internal static class EnginePerformance
{
    internal static int Run(string root, string bundled, string guard, string output)
    {
        var measurements = new List<object>(); string? failure = null; bool restored = false;
        try { Execute().GetAwaiter().GetResult(); } catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = failure is null && restored, MutationAttempted = true, Restored = restored, Failure = failure, Measurements = measurements }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null && restored ? 0 : 1;
        async Task Execute()
        {
            Directory.CreateDirectory(root);
            using var owner = new StandardPointerOwner.Client(root);
            var clock = new Clock();
            using var engine = new WindowsCursorEngine(Path.Combine(root, "state"), guard, clock);
            var theme = new ThemePreparer(bundled).Prepare(new AppConfiguration { Theme = "bibata-ice" }, root);
            long sourceBytes = theme.EstimatedBytes;
            var apply = await engine.ApplyThemeAsync(theme, 16).WaitAsync(TimeSpan.FromSeconds(20));
            if (!apply.Applied) throw new InvalidOperationException(apply.Error);
            engine.Observe(new(CursorRole.Arrow, true, (nint)111, 32));
            engine.SetScale(new(1, clock.Now, 201));
            await Task.Delay(2000); // Warm JIT, GC and initial publications before the idle measurement.
            await Measure("BibataArrow32Idle", 5000);
            engine.Observe(new(CursorRole.Wait, true, (nint)111, 32));
            engine.SetScale(new(16, clock.Now, 202));
            await Task.Delay(1000); // Populate actual SVG raster/native caches at the configured default maximum.
            await Measure("BibataWait512", 4000);
            var stop = await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(20)); restored = stop.AllOwnedRestored;
            if (!restored) throw new InvalidOperationException("Performance test did not restore owned roles.");
            async Task Measure(string label, int milliseconds)
            {
                using var process = Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime; long before = engine.PublishedFrames; var timer = Stopwatch.StartNew();
                await Task.Delay(milliseconds); process.Refresh();
                double elapsed = timer.Elapsed.TotalMilliseconds, consumed = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                long publications = engine.PublishedFrames - before;
                measurements.Add(new { Scenario = label, WallMilliseconds = elapsed, CpuMilliseconds = consumed, CorePercent = consumed / elapsed * 100, MachinePercent = consumed / elapsed * 100 / Environment.ProcessorCount, Publications = publications, PublicationsPerSecond = publications / elapsed * 1000, SourceBytes = sourceBytes, engine.RasterBytes, engine.BaselineBytes, MaximumPublicationLatencyMilliseconds = engine.MaximumPublicationLatency.TotalMilliseconds });
                if (publications > Math.Ceiling(elapsed / 1000 * 60) + 2) throw new InvalidOperationException("Publication limit exceeded.");
                if (engine.Status != CursorEngineStatus.Available) throw new InvalidOperationException("Renderer became unavailable during measurement.");
            }
        }
    }
    private sealed class Clock : IMonotonicClock { private readonly Stopwatch clock = Stopwatch.StartNew(); public TimeSpan Now => clock.Elapsed; }
}
