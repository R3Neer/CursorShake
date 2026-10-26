using System.Collections.Concurrent;
using System.Diagnostics;
using CursorShake.Core.Cursors;
using CursorShake.Core.Input;
using CursorShake.Core.Runtime;
using CursorShake.Core.Shake;
using CursorShake.Windows.Input;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class PointerRuntimeLoopTests
{
    [Fact]
    public async Task DeadlineWakeupsAdvanceTheDomainAfterMouseMovementStops()
    {
        var clock = new RuntimeTestClock();
        var input = NewInput(); var engine = new RuntimeTestEngine();
        using var detector = new ShakeDetector(input, engine, clock);
        using var loop = new PointerRuntimeLoop(clock, input, detector);
        for (int i = 0; i <= 4; i++)
        {
            clock.Now = TimeSpan.FromMilliseconds(i * 100);
            loop.Accept(new(RawPointerKind.Move, i % 2 == 0 ? 0 : 40, 0));
            await loop.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(GesturePhase.Growing, detector.Phase);
        clock.Now = TimeSpan.FromMilliseconds(530);
        await loop.InvokeAsync(() => { });
        await Eventually(loop, () => detector.Phase == GesturePhase.Holding);
        clock.Now = TimeSpan.FromMilliseconds(900);
        await loop.InvokeAsync(() => { });
        await Eventually(loop, () => detector.Phase == GesturePhase.Returning);
        clock.Now = TimeSpan.FromMilliseconds(1150);
        await loop.InvokeAsync(() => { });
        await Eventually(loop, () => detector.Phase == GesturePhase.Idle && detector.Scale == 1);
        Assert.All(engine.Samples, sample => Assert.InRange(sample.Scale, 1, 16));
    }

    [Fact]
    public async Task BacklogDiscardsStaleMovementButNeverDropsAButtonDown()
    {
        var clock = new RuntimeTestClock();
        int held = 0, discontinuities = 0;
        var input = new PointerInput(new GestureProfile(), () => (PointerButtons)Volatile.Read(ref held), (_, _) => (1, 96));
        input.Discontinuity += _ => Interlocked.Increment(ref discontinuities);
        using var detector = new ShakeDetector(input, new RuntimeTestEngine(), clock);
        using var loop = new PointerRuntimeLoop(clock, input, detector);
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var blocked = loop.InvokeAsync(() => { started.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            for (int i = 0; i < 2100; i++) loop.Accept(new(RawPointerKind.Move, i % 2 == 0 ? 0 : 40, 0));
            clock.Now = TimeSpan.FromMilliseconds(200);
            Volatile.Write(ref held, (int)PointerButtons.Right);
            loop.Accept(new(RawPointerKind.RightDown, 0, 0));
        }
        finally { release.Set(); }
        await blocked.WaitAsync(TimeSpan.FromSeconds(5));
        await loop.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(discontinuities > 0);
        Assert.Equal(PointerButtons.Right, input.CurrentButtons);
        Assert.Equal(GesturePhase.Blocked, detector.Phase);
        Assert.Equal(0, detector.Count);
        Assert.Equal(1, detector.Scale);
    }

    [Fact]
    public async Task FailedCommandIsReportedToCallerAndDoesNotKillTheSerialLoop()
    {
        var clock = new RuntimeTestClock(); var input = NewInput();
        using var detector = new ShakeDetector(input, new RuntimeTestEngine(), clock);
        using var loop = new PointerRuntimeLoop(clock, input, detector);
        await Assert.ThrowsAsync<InvalidOperationException>(() => loop.InvokeAsync(() => throw new InvalidOperationException("expected")));
        bool reached = false;
        await loop.InvokeAsync(() => reached = true).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(reached);
    }

    [Fact]
    public async Task DisposalDrainsAdmittedCommandsAndRejectsNewCommands()
    {
        var clock = new RuntimeTestClock(); var input = NewInput();
        using var detector = new ShakeDetector(input, new RuntimeTestEngine(), clock);
        var loop = new PointerRuntimeLoop(clock, input, detector);
        int executed = 0;
        var first = loop.InvokeAsync(() => Interlocked.Increment(ref executed));
        var second = loop.InvokeAsync(() => Interlocked.Increment(ref executed));
        loop.Dispose();
        await Task.WhenAll(first, second);
        Assert.Equal(2, executed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => loop.InvokeAsync(() => { }));
        loop.Accept(new(RawPointerKind.Move, 100, 0));
        Assert.Equal(2, executed);
    }

    [Fact]
    public async Task DisposalStopsDeadlineAdmissionEvenWhenTheDetectorHasAnOverdueReturnDeadline()
    {
        var clock = new RuntimeTestClock();
        var input = NewInput(); var engine = new RuntimeTestEngine();
        var detector = new ShakeDetector(input, engine, clock);
        var loop = new PointerRuntimeLoop(clock, input, detector);
        await loop.InvokeAsync(() =>
        {
            Assert.True(detector.TryStartPreview(clock.Now));
            clock.Now = TimeSpan.FromMilliseconds(300);
            detector.Advance(clock.Now);
            detector.SetSuspended(true, clock.Now);
            Assert.Equal(GesturePhase.Returning, detector.Phase);
            detector.Dispose();
            clock.Now = TimeSpan.FromSeconds(2); // The retained return deadline is now overdue.
        });
        var teardown = Task.Run(loop.Dispose);
        await teardown.WaitAsync(TimeSpan.FromSeconds(3));
        int samplesAtStop = engine.Samples.Count;
        loop.Accept(new(RawPointerKind.Move, 40, 0));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => loop.InvokeAsync(() => { }));
        Assert.Equal(samplesAtStop, engine.Samples.Count);
    }

    [Fact]
    public async Task DelayedDeadlineDoesNotConsumeTheInactivityBoundaryBeforeAnAlreadyAdmittedStroke()
    {
        var clock = new RuntimeTestClock();
        var semanticInput = new RuntimeSemanticInput();
        var engine = new RuntimeTestEngine();
        using var detector = new ShakeDetector(semanticInput, engine, clock);
        using var loop = new PointerRuntimeLoop(clock, NewInput(), detector); // Raw input is deliberately unused: this isolates FIFO/deadline admission.
        using var deadlineEntered = new ManualResetEventSlim();
        using var releaseDeadline = new ManualResetEventSlim();
        var errors = new ConcurrentQueue<Exception>();
        loop.Error += errors.Enqueue;
        loop.BeforeDeadline = admittedAt =>
        {
            if (admittedAt != TimeSpan.FromMilliseconds(680)) return;
            deadlineEntered.Set();
            if (!releaseDeadline.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The delayed deadline test was not released.");
        };
        await loop.InvokeAsync(() =>
        {
            semanticInput.EmitStroke(TimeSpan.Zero);
            clock.Now = TimeSpan.FromMilliseconds(100); semanticInput.EmitStroke(clock.Now);
            clock.Now = TimeSpan.FromMilliseconds(200); semanticInput.EmitStroke(clock.Now);
            clock.Now = TimeSpan.FromMilliseconds(680); // This admits an overdue wake at680, before the inactivity boundary700.
        });
        Assert.True(deadlineEntered.Wait(TimeSpan.FromSeconds(5)));
        double resumedScale = 0;
        GesturePhase resumedPhase = GesturePhase.Idle;
        Task stroke;
        try
        {
            clock.Now = TimeSpan.FromMilliseconds(690);
            stroke = loop.InvokeAsync(() =>
            {
                semanticInput.EmitStroke(TimeSpan.FromMilliseconds(690));
                resumedScale = detector.Scale;
                resumedPhase = detector.Phase;
            });
            clock.Now = TimeSpan.FromMilliseconds(740); // Processing is late; the wake must not consume the stroke's future boundary700.
        }
        finally { releaseDeadline.Set(); }
        await stroke.WaitAsync(TimeSpan.FromSeconds(5));
        // Growth from200 to330 at1.3cadence, then held: an admitted stroke690 must resume this height without a return dip.
        double progress = 130.0 * 1.3 / 1200;
        double expectedHeldScale = 1 + 15 * progress * progress * (3 - 2 * progress);
        Assert.Equal(expectedHeldScale, resumedScale, 8);
        Assert.Equal(GesturePhase.Growing, resumedPhase);
        Assert.Empty(errors);
        var samples = engine.Samples.ToArray();
        Assert.All(samples.Where(sample => sample.EvaluatedAt >= TimeSpan.FromMilliseconds(740)), sample => Assert.True(sample.Scale >= expectedHeldScale - 1e-8));
        for (int index = 1; index < samples.Length; index++)
            Assert.True(samples[index].EvaluatedAt >= samples[index - 1].EvaluatedAt, "Delayed semantic replay must never publish a retrospective sample.");
    }

    [Theory]
    [InlineData("Advance")]
    [InlineData("Preview")]
    [InlineData("Discontinuity")]
    [InlineData("Profile")]
    public async Task CommandOccurrenceContextKeepsAQueuedDeadlineOrderedWhenTheClockAdvances(string operation)
    {
        var clock = new RuntimeTestClock(); var input = NewInput(); var engine = new RuntimeTestEngine();
        var profile = operation == "Profile" ? new GestureProfile { Growth = new CursorShake.Core.Growth.GrowthProfile { GrowthDuration = TimeSpan.FromMilliseconds(1), ReturnDuration = TimeSpan.FromMilliseconds(1) } } : new GestureProfile();
        using var detector = new ShakeDetector(input, engine, clock, profile);
        using var loop = new PointerRuntimeLoop(clock, input, detector);
        var errors = new ConcurrentQueue<Exception>(); loop.Error += errors.Enqueue;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var seed = loop.InvokeAsync(() =>
        {
            Assert.True(detector.TryStartPreview(TimeSpan.Zero));
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Seed command was not released.");
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task change;
        try
        {
            clock.Now = TimeSpan.FromMilliseconds(20);
            change = loop.InvokeAsync(time =>
            {
                clock.Now = TimeSpan.FromMilliseconds(40);
                Assert.Equal(TimeSpan.FromMilliseconds(20), time.OccurredAt);
                Assert.True(time.ApplyAt >= time.OccurredAt);
                switch (operation)
                {
                    case "Advance": detector.Advance(time.OccurredAt, time.ApplyAt); break;
                    case "Preview": Assert.False(detector.TryStartPreview(time.OccurredAt, time.ApplyAt)); break;
                    case "Discontinuity": input.SignalDiscontinuity(time.OccurredAt); break;
                    case "Profile":
                        detector.ApplyProfile(new GestureProfile(), time.OccurredAt, time.ApplyAt);

                        break;
                }
            });
        }
        finally { release.Set(); }
        await Task.WhenAll(seed, change).WaitAsync(TimeSpan.FromSeconds(5));
        // Seed completion admitted a wake at20 behind the command; its occurrence must stay20 even if processing takes longer.
        await loop.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(errors);
        if (operation is "Advance" or "Preview") Assert.True(detector.IsPreview);
        loop.Accept(new(RawPointerKind.LeftDown, 0, 0));
        await loop.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PointerButtons.Left, input.CurrentButtons);
        Assert.Equal(GesturePhase.Blocked, detector.Phase);
        Assert.Empty(errors);
        AssertMonotonicSamples(engine);
    }

    [Fact]
    public async Task StaleMovementDiscontinuityCannotMakeTheFollowingQueuedDeadlineGoBackInTime()
    {
        var clock = new RuntimeTestClock(); int held = 0;
        var input = new PointerInput(new GestureProfile(), () => (PointerButtons)Volatile.Read(ref held), (_, _) => (1, 96));
        var engine = new RuntimeTestEngine();
        int processingDelay = 0;
        input.Discontinuity += _ => { if (Interlocked.Exchange(ref processingDelay, 1) == 0) clock.Now = TimeSpan.FromMilliseconds(220); };
        using var detector = new ShakeDetector(input, engine, clock);
        using var loop = new PointerRuntimeLoop(clock, input, detector);
        var errors = new ConcurrentQueue<Exception>(); loop.Error += errors.Enqueue;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var seed = loop.InvokeAsync(() =>
        {
            Assert.True(detector.TryStartPreview(TimeSpan.Zero));
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Seed command was not released.");
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task lateCommand;
        try
        {
            clock.Now = TimeSpan.FromMilliseconds(20);
            loop.Accept(new(RawPointerKind.Move, 100, 0));
            Volatile.Write(ref held, (int)PointerButtons.Left);
            loop.Accept(new(RawPointerKind.LeftDown, 100, 0));
            lateCommand = loop.InvokeAsync(() =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(220), clock.Now);
            });
            clock.Now = TimeSpan.FromMilliseconds(200);
        }
        finally { release.Set(); }
        await Task.WhenAll(seed, lateCommand).WaitAsync(TimeSpan.FromSeconds(5));
        await loop.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(errors);
        Assert.Equal(PointerButtons.Left, input.CurrentButtons);
        Assert.Equal(GesturePhase.Blocked, detector.Phase);
        Assert.Equal(0, detector.Count);
        clock.Now = TimeSpan.FromMilliseconds(500);
        await loop.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
        await Eventually(loop, () => detector.Scale == 1);
        Assert.Empty(errors); // Continued progress must neither flood errors nor get stuck on a stale deadline.
        AssertMonotonicSamples(engine);
    }

    private static void AssertMonotonicSamples(RuntimeTestEngine engine)
    {
        var samples = engine.Samples.ToArray();
        for (int index = 1; index < samples.Length; index++)
            Assert.True(samples[index].EvaluatedAt >= samples[index - 1].EvaluatedAt, "Runtime processing must never publish a retrospective sample.");
    }
    private static PointerInput NewInput() => new(new GestureProfile(), () => PointerButtons.None, (_, _) => (1, 96));
    private static async Task Eventually(PointerRuntimeLoop loop, Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            bool complete = false;
            await loop.InvokeAsync(() => complete = condition()).WaitAsync(TimeSpan.FromSeconds(5));
            if (complete) return;
            await Task.Yield();
        }
        Assert.Fail("The live serial loop did not reach the expected deadline state.");
    }
}

internal sealed class RuntimeTestClock : IMonotonicClock
{
    private long ticks;
    public TimeSpan Now { get => TimeSpan.FromTicks(Interlocked.Read(ref ticks)); set => Interlocked.Exchange(ref ticks, value.Ticks); }
}
internal sealed class RuntimeTestEngine : ICursorEngine
{
    public ConcurrentQueue<ScaleSample> Samples { get; } = new();
    public void SetScale(ScaleSample sample) => Samples.Enqueue(sample);
}

internal sealed class RuntimeSemanticInput : IPointerInput
{
    public event Action<ShakeStroke>? Stroke;
    public event Action<ButtonsChanged>? ButtonsChanged { add { } remove { } }
    public event Action<InputDiscontinuity>? Discontinuity { add { } remove { } }
    public PointerButtons CurrentButtons => PointerButtons.None;
    public void Reset() { }
    internal void EmitStroke(TimeSpan occurredAt) => Stroke?.Invoke(new(occurredAt));
}
