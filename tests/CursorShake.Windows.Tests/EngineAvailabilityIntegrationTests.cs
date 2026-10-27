using CursorShake.App;
using CursorShake.Core.Growth;
using CursorShake.Core.Input;
using CursorShake.Core.Shake;
using CursorShake.Windows.Input;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class EngineAvailabilityIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AQueuedOwnershipLossCannotBeErasedByAFastRecoveryAndNeverReplaysTheOldGrowth(bool anotherSuspensionReason)
    {
        var clock = new RuntimeTestClock(); var semantic = new RuntimeSemanticInput(); var engine = new RuntimeTestEngine();
        var profile = new GestureProfile { Growth = new GrowthProfile { GrowthDuration = TimeSpan.FromMilliseconds(200) } };
        using var detector = new ShakeDetector(semantic, engine, clock, profile);
        var raw = new PointerInput(profile, () => PointerButtons.None, (_, _) => (1, 96));
        using var runtime = new PointerRuntimeLoop(clock, raw, detector);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        long oldEpoch = -1; int oldSampleCount = -1; int currentUnavailable = 0, otherReason = 0;
        var seed = runtime.InvokeAsync(() =>
        {
            semantic.EmitStroke(TimeSpan.Zero);
            clock.Now = TimeSpan.FromMilliseconds(100); semantic.EmitStroke(clock.Now);
            clock.Now = TimeSpan.FromMilliseconds(200); semantic.EmitStroke(clock.Now);
            clock.Now = TimeSpan.FromMilliseconds(300); detector.Advance(clock.Now);
            Assert.Equal(GesturePhase.Growing, detector.Phase); Assert.True(detector.Scale > 2);
            oldEpoch = detector.GestureEpoch; oldSampleCount = engine.Samples.Count;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The domain fixture was not released.");
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task unavailable, available;
        try
        {
            clock.Now = TimeSpan.FromMilliseconds(310); Volatile.Write(ref currentUnavailable, 1);
            unavailable = AppHost.QueueEngineAvailability(runtime, detector, true, () => Volatile.Read(ref currentUnavailable) != 0 || Volatile.Read(ref otherReason) != 0);
            clock.Now = TimeSpan.FromMilliseconds(320); Volatile.Write(ref currentUnavailable, 0);
            Volatile.Write(ref otherReason, anotherSuspensionReason ? 1 : 0);
            available = AppHost.QueueEngineAvailability(runtime, detector, false, () => Volatile.Read(ref currentUnavailable) != 0 || Volatile.Read(ref otherReason) != 0);
        }
        finally { release.Set(); }
        await Task.WhenAll(seed, unavailable, available).WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.InvokeAsync(() =>
        {
            Assert.True(detector.GestureEpoch > oldEpoch);
            Assert.Equal(0, detector.Count); Assert.Equal(1, detector.Scale);
            Assert.Equal(anotherSuspensionReason, detector.IsSuspended);
        });
        Assert.All(engine.Samples.Skip(oldSampleCount), sample => Assert.Equal(1, sample.Scale));
        if (anotherSuspensionReason)
        {
            clock.Now = TimeSpan.FromMilliseconds(330);
            await runtime.InvokeAsync(() => { semantic.EmitStroke(clock.Now); Assert.Equal(0, detector.Count); });
            Volatile.Write(ref otherReason, 0);
            await AppHost.QueueEngineAvailability(runtime, detector, false, () => false);
        }
        for (int count = 1; count <= 2; count++)
        {
            clock.Now = TimeSpan.FromMilliseconds(400 + count * 100);
            int expected = count;
            await runtime.InvokeAsync(() => { semantic.EmitStroke(clock.Now); Assert.Equal(expected, detector.Count); Assert.Equal(1, detector.Scale); });
        }
        Assert.All(engine.Samples.Skip(oldSampleCount), sample => Assert.Equal(1, sample.Scale));
        clock.Now = TimeSpan.FromMilliseconds(700);
        await runtime.InvokeAsync(() => { semantic.EmitStroke(clock.Now); Assert.Equal(GesturePhase.Growing, detector.Phase); });
        clock.Now = TimeSpan.FromMilliseconds(750);
        await runtime.InvokeAsync(time => detector.Advance(time.OccurredAt, time.ApplyAt));
        Assert.True(detector.Scale > 1); // Only a new full activation may grow again.
    }
}
