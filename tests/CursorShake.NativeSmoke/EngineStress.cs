using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CursorShake.Core.Cursors;
using CursorShake.Core.Runtime;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Formats;
using CursorShake.Windows.Cursors.Native;

namespace CursorShake.NativeSmoke;

internal static class EngineStress
{
    internal static int Run(string root, string guard, string output)
    {
        var cases = new List<object>(); string? failure = null; bool restored = false;
        try { Execute().GetAwaiter().GetResult(); } catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = failure is null && restored, MutationAttempted = true, Restored = restored, Failure = failure, Cases = cases }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null && restored ? 0 : 1;
        async Task Execute()
        {
            Directory.CreateDirectory(root);
            using var owner = new StandardPointerOwner.Client(root);
            var clock = new LiveClock();
            using var engine = new WindowsCursorEngine(Path.Combine(root, "state"), guard, clock);
            var sizes = new ConcurrentDictionary<int, int>();
            var first = new LightVectorFrame(37, sizes);
            var staticAsset = new CursorAsset([first], [new(0, TimeSpan.FromMilliseconds(100))]);
            var initial = await engine.ApplyThemeAsync(new("stress-static", CursorRoles.All.ToDictionary(r => r, _ => staticAsset)), 16).WaitAsync(TimeSpan.FromSeconds(15));
            Need(initial.Applied, initial.Error ?? "Initial theme failed.");
            engine.Observe(new(CursorRole.Arrow, true, (nint)111, 32));
            await Wait(() => sizes.ContainsKey(32));
            await Task.Delay(100); // Allow admitted normalizations to finish before measuring the idle interval.
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime; var interval = Stopwatch.StartNew();
            long before = engine.PublishedFrames;
            await Task.Delay(600);
            process.Refresh();
            cases.Add(new { Step = "Idle", Milliseconds = interval.Elapsed.TotalMilliseconds, CpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds, Publications = engine.PublishedFrames - before });
            engine.SetScale(new(16, clock.Now, 100));
            await Wait(() => sizes.ContainsKey(512));
            engine.Observe(new(CursorRole.Arrow, true, (nint)222, 128));
            await Wait(() => sizes.ContainsKey(2048));
            Need(engine.Status == CursorEngineStatus.Available, "2048px must be available.");
            cases.Add(new { Step = "Sizes", Sizes = sizes.Keys.Order().ToArray(), engine.RasterBytes, PublicationLatencyMilliseconds = engine.MaximumPublicationLatency.TotalMilliseconds });
            Need(engine.RasterBytes <= 64L * 1024 * 1024, "Raster budget exceeded64MiB.");
            engine.Observe(new(CursorRole.Arrow, true, (nint)333, 129));
            Need(engine.Status == CursorEngineStatus.Incompatible, "A DPI whose configured maximum exceeds2048px must be rejected.");
            engine.Observe(new(CursorRole.Arrow, true, (nint)222, 128));
            engine.SetScale(new(1, clock.Now, 101));
            var animated = new CursorAsset([new LightVectorFrame(53, sizes), new LightVectorFrame(181, sizes)], [new(0, TimeSpan.FromMilliseconds(1)), new(1, TimeSpan.FromMilliseconds(1))]);
            var apply = await engine.ApplyThemeAsync(new("stress-animated", CursorRoles.All.ToDictionary(r => r, _ => animated)), 16).WaitAsync(TimeSpan.FromSeconds(15));
            Need(apply.Applied, apply.Error ?? "Animated stress theme failed.");
            engine.Observe(new(CursorRole.Arrow, true, (nint)222, 128));
            engine.SetScale(new(16, clock.Now, 102));
            await Wait(() => engine.RasterBytes >= 2048L * 2048 * 4);
            before = engine.PublishedFrames; cpu = process.TotalProcessorTime; interval.Restart();
            await Task.Delay(1200);
            process.Refresh(); long publications = engine.PublishedFrames - before; double seconds = interval.Elapsed.TotalSeconds;
            cases.Add(new { Step = "Animated2048", Seconds = seconds, Publications = publications, PublicationsPerSecond = publications / seconds, CpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds, engine.RasterBytes, PublicationLatencyMilliseconds = engine.MaximumPublicationLatency.TotalMilliseconds });
            Need(publications <= Math.Ceiling(seconds * 60) + 2, "Renderer exceeded60publications/second.");
            Need(engine.RasterBytes <= 64L * 1024 * 1024, "Animated raster budget exceeded64MiB.");
            var stop = await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(20)); restored = stop.AllOwnedRestored;
            Need(restored, "Stress stop did not restore all owned roles.");
            long stopped = engine.PublishedFrames;
            engine.SetScale(new(16, clock.Now, 103)); engine.Observe(new(CursorRole.Wait, true, (nint)444, 128));
            Need(engine.PublishedFrames == stopped && engine.Status == CursorEngineStatus.Stopped, "Stopped renderer must not publish more frames.");
        }
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> done)
    {
        var timeout = Stopwatch.StartNew();
        while (!done()) { if (timeout.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Engine stress target was not reached."); await Task.Delay(10); }
    }
    private sealed class LiveClock : IMonotonicClock { private readonly Stopwatch watch = Stopwatch.StartNew(); public TimeSpan Now => watch.Elapsed; }
    private sealed class LightVectorFrame(byte blue, ConcurrentDictionary<int, int> sizes) : CursorFrame(32, 32, 1, 1)
    {
        public override bool IsVector => true;
        public override long EstimatedBytes => 128;
        public override CursorPixels Rasterize(int width, int height) { ValidateSize(width, height); sizes.AddOrUpdate(width, 1, (_, count) => count + 1); return CrashGuardSmoke.Colored(width, blue); }
        public override void Dispose() { }
    }
}
