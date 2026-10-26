using System.Diagnostics;
using System.Text.Json;
using CursorShake.Core.Cursors;
using CursorShake.Core.Runtime;
using CursorShake.Core.Shake;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Formats;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Input;

namespace CursorShake.NativeSmoke;

// Deliberately excluded from xUnit: running this mode publishes system cursors and requires exclusive root coordination.
internal static class EngineSmoke
{
    internal static int Run(string root, string bundled, string guard, string output)
    {
        var observations = new List<object>();
        string? failure = null;
        bool restored = false;
        try { Execute().GetAwaiter().GetResult(); }
        catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { MutationAttempted = true, Passed = failure is null && restored, Restored = restored, Failure = failure, Observations = observations }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null && restored ? 0 : 1;

        async Task Execute()
        {
            Directory.CreateDirectory(root);
            using var owner = new StandardPointerOwner.Client(root, 32514); // WM_SETCURSOR selects the shared Wait role in another process.
            var clock = new SmokeClock();
            using var engine = new WindowsCursorEngine(Path.Combine(root, "state"), guard, clock);
            try
            {
            engine.ObserveSystemCursor();
            Require(engine.Status == CursorEngineStatus.Starting, "Observing a system cursor before applying a theme must remain Starting.");
            observations.Add(new { Step = "BeforeInitialTheme", engine.Status });
            var input = new PointerInput(new GestureProfile(), () => CursorShake.Core.Input.PointerButtons.None);
            using var detector = new ShakeDetector(input, engine, clock);
            using var runtime = new PointerRuntimeLoop(clock, input, detector);
            var workspace = new WorkspaceStore(Path.Combine(root, "workspace"), Path.Combine(root, "locator"), persistLocator: false);
            await using (var coordinator = new ConfigurationCoordinator(workspace, new ThemePreparer(bundled), engine, input, detector, runtime, clock, () => false))
            {
                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                engine.BeforeThemePublish = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Theme publication test gate was not released."); };
                var initialization = coordinator.InitializeAsync();
                try
                {
                    if (!entered.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Coordinator did not enter native publication.");
                    bool suspended = false;
                    await runtime.InvokeAsync(() => suspended = detector.IsSuspended);
                    Require(coordinator.IsApplying && suspended, "Input must remain suspended during native theme application.");
                    observations.Add(new { Step = "Applying", coordinator.IsApplying, Suspended = suspended });
                }
                finally { release.Set(); }
                await initialization.WaitAsync(TimeSpan.FromSeconds(20));
                engine.BeforeThemePublish = null;
                Require(coordinator.Effective is not null && !coordinator.IsApplying, "Coordinator must commit configuration and clear IsApplying.");
                await runtime.InvokeAsync(() => Require(!detector.IsSuspended, "Successful application must release the temporary suspension."));
            }
            engine.Observe(new(CursorRole.Arrow, true, (nint)111, 32));
            clock.Set(TimeSpan.FromSeconds(10));
            engine.Observe(new(CursorRole.Wait, true, (nint)111, 32));
            var first = engine.Snapshot;
            clock.Set(TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(95));
            engine.Observe(new(CursorRole.Wait, true, (nint)222, 48));
            var changedMonitor = engine.Snapshot;
            Require(first.ActivatedAt == changedMonitor.ActivatedAt, "Changing monitor/DPI must preserve the ANI time origin.");
            Require(changedMonitor.HardEpoch > first.HardEpoch, "Changing monitor must invalidate pending publications.");
            observations.Add(new { Step = "Monitor", Before = first, After = changedMonitor, Elapsed = clock.Now - changedMonitor.ActivatedAt });
            using (var phaseReference = new ThemePreparer(bundled).Prepare(new AppConfiguration(), workspace.RootPath))
            {
                var ani = phaseReference.Roles[CursorRole.Wait];
                Require(ani.IsAnimated, "Wait fixture must contain animation.");
                Require(ani.FrameIndexAt(clock.Now - changedMonitor.ActivatedAt) == ani.FrameIndexAt(TimeSpan.FromMilliseconds(95)), "ANI phase must include time before monitor change.");
            }
            foreach (var role in CursorRoles.All)
            {
                clock.Set(clock.Now + TimeSpan.FromMilliseconds(20));
                engine.Observe(new(role, true, (nint)222, 48));
                Require(engine.Snapshot.ActiveRole == role && engine.Status == CursorEngineStatus.Available, "Standard cursor role must remain available: " + role);
            }
            clock.StartRealTime(); // Preserve deterministic ANI assertions above while allowing the renderer publication clock to keep advancing.
            // A native publication can be expensive; input/domain scale requests must never wait for that native gate.
            engine.Observe(new(CursorRole.Arrow, true, (nint)222, 48));
            using (var nativeEntered = new ManualResetEventSlim())
            using (var releaseNative = new ManualResetEventSlim())
            using (var nativeCallbackDone = new ManualResetEventSlim())
            {
                int blockedOnce = 0;
                engine.BeforeNativePublish = role =>
                {
                    if (role != CursorRole.Arrow || Interlocked.Exchange(ref blockedOnce, 1) != 0) return;
                    nativeEntered.Set();
                    try { if (!releaseNative.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Blocked native publication was not released."); }
                    finally { nativeCallbackDone.Set(); }
                };
                clock.Set(clock.Now + TimeSpan.FromMilliseconds(20));
                engine.SetScale(new ScaleSample(2, clock.Now, 81));
                try
                {
                    Require(nativeEntered.Wait(TimeSpan.FromSeconds(5)), "Native publication must be blocked before measuring a new scale request.");
                    var latency = await Task.Run(() =>
                    {
                        clock.Set(clock.Now + TimeSpan.FromMilliseconds(20));
                        var measured = Stopwatch.StartNew();
                        engine.SetScale(new ScaleSample(3, clock.Now, 81));
                        return measured.Elapsed.TotalMilliseconds;
                    }).WaitAsync(TimeSpan.FromSeconds(1));
                    Require(latency < 100, "Domain scale request blocked behind native publication for100ms or more.");
                    await Task.Delay(300); // Deliberate integration fixture: keep native publication blocked after the domain request has returned.
                    observations.Add(new { Step = "ScaleRequestDuringBlockedNativePublish", CallMilliseconds = latency, NativeGateRemainedBlocked = !releaseNative.IsSet });
                }
                finally
                {
                    releaseNative.Set(); engine.BeforeNativePublish = null;
                    if (Volatile.Read(ref blockedOnce) != 0) Require(nativeCallbackDone.Wait(TimeSpan.FromSeconds(5)), "Native test callback did not leave its gate safely.");
                }
            }
            clock.Set(clock.Now + TimeSpan.FromMilliseconds(20));
            engine.SetScale(new ScaleSample(1, clock.Now, 82));
            // Explicit injected decode failure occurs only after all initial 48px publications succeeded.
            var frame = new FailWhenScaledFrame(48);
            var asset = new CursorAsset([frame], [new CursorStep(0, TimeSpan.FromMilliseconds(100))]);
            var faultTheme = new PreparedTheme("injected-render-failure", CursorRoles.All.ToDictionary(role => role, _ => asset));
            var applied = await engine.ApplyThemeAsync(faultTheme, 2).WaitAsync(TimeSpan.FromSeconds(15));
            Require(applied.Applied, "Fault fixture must first publish successfully at normal size.");
            using var degraded = new ManualResetEventSlim();
            engine.StatusChanged += state => { if (state == CursorEngineStatus.Degraded) degraded.Set(); };
            engine.Observe(new(CursorRole.Arrow, true, (nint)222, 48));
            clock.Set(clock.Now + TimeSpan.FromSeconds(1));
            engine.SetScale(new ScaleSample(2, clock.Now, 91));
            if (!degraded.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Injected raster failure did not enter Degraded.");
            var failed = engine.Snapshot;
            engine.Observe(new(CursorRole.Wait, true, (nint)333, 32));
            engine.Normalize();
            var rejected = await engine.ApplyThemeAsync(faultTheme, 2).WaitAsync(TimeSpan.FromSeconds(10)); // FIFO barrier after Normalize.
            Require(!rejected.Applied && engine.Status == CursorEngineStatus.Degraded, "Observing and normalizing must never clear Degraded.");
            Require(engine.PublishedFrames == failed.PublishedFrames, "Degraded engine must block subsequent native publications.");
            observations.Add(new { Step = "DegradedSticky", Before = failed, After = engine.Snapshot, engine.RasterBytes });
            }
            finally
            {
                var stop = await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
                restored = stop.AllOwnedRestored;
                Require(restored, "All owned roles must be restored after injected renderer failure.");
            }
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class SmokeClock : IMonotonicClock
    {
        private long ticks;
        private readonly Stopwatch wallTime = new();
        public TimeSpan Now => TimeSpan.FromTicks(Interlocked.Read(ref ticks) + wallTime.Elapsed.Ticks);
        internal void Set(TimeSpan now) => Interlocked.Exchange(ref ticks, now.Ticks - wallTime.Elapsed.Ticks);
        internal void StartRealTime() => wallTime.Start();
    }
    private sealed class FailWhenScaledFrame(int maximumSide) : CursorFrame(32, 32, 1, 1)
    {
        public override bool IsVector => true;
        public override long EstimatedBytes => 128;
        public override CursorPixels Rasterize(int width, int height)
        {
            if (Math.Max(width, height) > maximumSide) throw new InvalidDataException("Deliberate scaled-frame failure for recovery QA.");
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 255; pixels[i + 3] = 255; }
            return new(width, height, 1, 1, pixels, new byte[((width + 31) / 32) * 4 * height]);
        }
        public override void Dispose() { }
    }
}
