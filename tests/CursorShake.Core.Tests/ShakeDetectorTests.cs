using CursorShake.Core.Growth;
using CursorShake.Core.Input;
using CursorShake.Core.Shake;
using Xunit;

namespace CursorShake.Core.Tests;

public sealed class ShakeDetectorTests
{
    [Fact]
    public void ActivationRequiresTheConfiguredCountRatherThanThreeAlways()
    {
        using var rig = new DetectorRig(new GestureProfile { CountToActivate = 5 });
        foreach (var time in new[] { 0, 100, 200, 300 }) rig.Stroke(time);
        Assert.Equal(1, rig.Detector.Scale);
        Assert.Equal("Counting", rig.Detector.Phase.ToString());
        rig.Stroke(400);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
        rig.Advance(450);
        Assert.True(rig.Detector.Scale > 1);
    }

    [Fact]
    public void IntervalBoundaryIsInclusive()
    {
        using var rig = new DetectorRig();
        rig.Stroke(0);
        rig.Stroke(130);
        rig.Stroke(260);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
    }

    [Fact]
    public void IntervalOverTheLimitRestartsTheSequence()
    {
        using var rig = new DetectorRig();
        rig.Stroke(0);
        rig.Stroke(131);
        rig.Stroke(262);
        Assert.Equal("Counting", rig.Detector.Phase.ToString());
        Assert.Equal(1, rig.Detector.Count);
        rig.Stroke(392);
        Assert.Equal("Counting", rig.Detector.Phase.ToString());
        rig.Stroke(522);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
    }

    [Fact]
    public void GraceIsIncludedInThePauseAndLateAdvanceRespectsEveryDeadline()
    {
        using var rig = Activated();
        rig.Advance(330);
        Assert.Equal("Holding", rig.Detector.Phase.ToString());
        var held = rig.Detector.Scale;
        Assert.True(held > 1);
        rig.Advance(699);
        Assert.Equal(held, rig.Detector.Scale);
        rig.Advance(700);
        Assert.Equal("Returning", rig.Detector.Phase.ToString());
        Assert.Equal(held, rig.Detector.Scale);
        rig.Advance(950);
        Assert.Equal(1, rig.Detector.Scale);
        Assert.Equal("Idle", rig.Detector.Phase.ToString());
    }

    [Fact]
    public void InactivityAndReturnDurationsAreConfigurable()
    {
        using var rig = Activated(new GestureProfile { Growth = new GrowthProfile { InactivityDelay = TimeSpan.FromMilliseconds(800), ReturnDuration = TimeSpan.FromMilliseconds(400) } });
        rig.Advance(999);
        var held = rig.Detector.Scale;
        Assert.Equal("Holding", rig.Detector.Phase.ToString());
        rig.Advance(1000);
        Assert.Equal("Returning", rig.Detector.Phase.ToString());
        rig.Advance(1200);
        Assert.InRange(rig.Detector.Scale, 1.000001, held - 0.000001);
        rig.Advance(1400);
        Assert.Equal(1, rig.Detector.Scale);
    }

    [Fact]
    public void FasterValidCadenceSpeedsGrowthWithoutChangingTheMaximum()
    {
        using var slow = new DetectorRig();
        using var fast = new DetectorRig();
        foreach (var time in new[] { 0, 130, 260 }) slow.Stroke(time);
        foreach (var time in new[] { 0, 50, 100 }) fast.Stroke(time);
        slow.Advance(360);
        fast.Advance(200);
        Assert.True(fast.Detector.Scale > slow.Detector.Scale);
        for (int t = 250; t <= 2050; t += 50) fast.Stroke(t);
        Assert.Equal(16, fast.Detector.Scale, 8);
        Assert.All(fast.Engine.Samples, s => Assert.InRange(s.Scale, 1, 16));
    }

    [Theory]
    [InlineData(PointerButtons.Left)]
    [InlineData(PointerButtons.Middle)]
    [InlineData(PointerButtons.Right)]
    public void AnySupportedButtonReturnsImmediatelyAndBlocksUntilReleased(PointerButtons button)
    {
        using var rig = Activated();
        rig.Advance(300);
        var before = rig.Detector.Scale;
        rig.Buttons(300, button);
        Assert.Equal(0, rig.Detector.Count);
        rig.Stroke(350);
        rig.Stroke(400);
        rig.Stroke(450);
        rig.Advance(550);
        Assert.Equal(1, rig.Detector.Scale);
        Assert.Equal(0, rig.Detector.Count);
        Assert.True(before > 1);
        rig.Buttons(600, PointerButtons.None);
        rig.Stroke(650);
        rig.Stroke(750);
        Assert.Equal("Counting", rig.Detector.Phase.ToString());
        rig.Stroke(850);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
    }

    [Fact]
    public void ReleasingOneOfSeveralHeldButtonsDoesNotUnblock()
    {
        using var rig = new DetectorRig();
        rig.Buttons(0, PointerButtons.Left | PointerButtons.Right);
        rig.Buttons(10, PointerButtons.Right);
        foreach (var time in new[] { 20, 30, 40 }) rig.Stroke(time);
        Assert.Equal(0, rig.Detector.Count);
        Assert.Equal(1, rig.Detector.Scale);
        rig.Buttons(50, PointerButtons.None);
        rig.Stroke(60);
        Assert.Equal(1, rig.Detector.Count);
    }

    [Fact]
    public void OneValidStrokeResumesBeforeReturnCompletesWithoutJump()
    {
        using var rig = Activated();
        rig.Advance(750);
        var returning = rig.Detector.Scale;
        Assert.True(returning > 1);
        rig.Stroke(750);
        Assert.Equal(returning, rig.Detector.Scale);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
        rig.Advance(800);
        Assert.True(rig.Detector.Scale > returning);
    }

    [Fact]
    public void AfterReturningToNormalTheFullCountIsRequiredAgain()
    {
        using var rig = Activated(new GestureProfile { CountToActivate = 4 });
        // Activated emits enough strokes for either the default or a configured count.
        rig.Advance(1050);
        Assert.Equal(1, rig.Detector.Scale);
        rig.Stroke(1100);
        rig.Stroke(1200);
        rig.Stroke(1300);
        Assert.Equal("Counting", rig.Detector.Phase.ToString());
        rig.Stroke(1400);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
    }

    [Fact]
    public void PreviewIgnoresShakeActivationAndRearmsTheConfiguredCount()
    {
        using var rig = new DetectorRig(new GestureProfile { CountToActivate = 4 });
        Assert.True(rig.Detector.TryStartPreview(TimeSpan.Zero));
        foreach (var time in new[] { 50, 100, 150, 200, 250 }) rig.Stroke(time);
        Assert.True(rig.Detector.IsPreview);
        Assert.Equal(0, rig.Detector.Count);
        rig.Advance(10000);
        Assert.False(rig.Detector.IsPreview);
        Assert.Equal(1, rig.Detector.Scale);
        foreach (var time in new[] { 10100, 10200, 10300 }) rig.Stroke(time);
        Assert.Equal("Counting", rig.Detector.Phase.ToString());
        rig.Stroke(10400);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
    }

    [Fact]
    public void PreviewIsRejectedWhileCountingOrButtonsAreHeld()
    {
        using var counting = new DetectorRig();
        counting.Stroke(0);
        Assert.False(counting.Detector.TryStartPreview(TimeSpan.Zero));
        using var held = new DetectorRig();
        held.Buttons(0, PointerButtons.Left);
        Assert.False(held.Detector.TryStartPreview(TimeSpan.Zero));
    }

    [Fact]
    public void SuspensionCancelsAndRequiresNewStrokesAfterResume()
    {
        using var rig = Activated();
        rig.Advance(300);
        rig.Detector.SetSuspended(true, rig.Clock.Now);
        foreach (var time in new[] { 350, 400, 450 }) rig.Stroke(time);
        rig.Advance(600);
        Assert.Equal(1, rig.Detector.Scale);
        Assert.True(rig.Detector.IsSuspended);
        Assert.False(rig.Detector.TryStartPreview(rig.Clock.Now));
        rig.Detector.SetSuspended(false, rig.Clock.Now);
        rig.Stroke(650);
        rig.Stroke(750);
        Assert.Equal("Counting", rig.Detector.Phase.ToString());
        rig.Stroke(850);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
    }

    [Fact]
    public void DiscontinuityDropsTheSequenceAndUsesTheCurrentButtonSnapshot()
    {
        using var rig = new DetectorRig();
        rig.Stroke(0);
        rig.Stroke(100);
        rig.Clock.Now = TimeSpan.FromMilliseconds(120);
        rig.Input.EmitDiscontinuity(rig.Clock.Now, PointerButtons.Right);
        rig.Stroke(200);
        rig.Stroke(250);
        rig.Stroke(300);
        Assert.Equal(0, rig.Detector.Count);
        Assert.Equal(1, rig.Detector.Scale);
        rig.Buttons(350, PointerButtons.None);
        rig.Stroke(400);
        Assert.Equal(1, rig.Detector.Count);
    }

    [Fact]
    public void ProfileCanBeAppliedAtNormalWithoutMixingOldCount()
    {
        using var rig = new DetectorRig();
        rig.Stroke(0);
        rig.Clock.Now = TimeSpan.FromMilliseconds(50);
        rig.Detector.ApplyProfile(new GestureProfile { CountToActivate = 2 }, rig.Clock.Now);
        Assert.Equal(0, rig.Detector.Count);
        rig.Stroke(100);
        Assert.Equal("Counting", rig.Detector.Phase.ToString());
        rig.Stroke(200);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
    }

    private static DetectorRig Activated(GestureProfile? profile = null)
    {
        var rig = new DetectorRig(profile);
        int count = profile?.CountToActivate ?? 3;
        for (int i = 0; i < count; i++) rig.Stroke(i * 100);
        return rig;
    }
}
