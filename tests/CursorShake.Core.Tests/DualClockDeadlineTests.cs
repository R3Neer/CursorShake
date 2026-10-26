using CursorShake.Core.Input;
using CursorShake.Core.Shake;

namespace CursorShake.Core.Tests;

public sealed class DualClockDeadlineTests
{
    [Fact] public void LateWakeCannotCrossInactivityAheadOfAnAlreadyAdmittedStroke()
    {
        using var rig = Active(); rig.Advance(330); double held = rig.Detector.Scale;
        rig.Clock.Now = Ms(740); rig.Detector.Advance(Ms(680), Ms(740));
        Assert.Equal(GesturePhase.Holding, rig.Detector.Phase); Assert.Equal(held, rig.Detector.Scale);
        rig.Input.EmitStroke(Ms(690));
        Assert.Equal(GesturePhase.Growing, rig.Detector.Phase); Assert.Equal(held, rig.Detector.Scale); Assert.Equal(0, rig.Detector.Count);
        Monotonic(rig);
    }
    [Fact] public void StrokeBeforeLogicalReturnCompletionResumesEvenIfAppliedAfterPhysicalCompletion()
    {
        using var rig = Active(); rig.Advance(700);
        rig.Clock.Now = Ms(1000); rig.Detector.Advance(Ms(930), Ms(1000));
        Assert.Equal(1, rig.Detector.Scale); Assert.Equal(GesturePhase.Returning, rig.Detector.Phase);
        rig.Input.EmitStroke(Ms(940));
        Assert.Equal(GesturePhase.Growing, rig.Detector.Phase); Assert.Equal(0, rig.Detector.Count);
        rig.Detector.Advance(Ms(940), Ms(1020)); Assert.True(rig.Detector.Scale > 1); Monotonic(rig);
    }
    [Theory] [InlineData(950)] [InlineData(960)]
    public void StrokeAtOrAfterLogicalReturnCompletionRequiresFullCount(double occurred)
    {
        using var rig = Active(); rig.Advance(700); rig.Clock.Now = Ms(1000); rig.Input.EmitStroke(Ms(occurred));
        Assert.Equal(GesturePhase.Counting, rig.Detector.Phase); Assert.Equal(1, rig.Detector.Count); Assert.Equal(1, rig.Detector.Scale); Monotonic(rig);
    }
    [Fact] public void PreviewDoesNotEndBeforeItsAdmittedLogicalCompletion()
    {
        using var rig = new DetectorRig(); Assert.True(rig.Detector.TryStartPreview(Ms(0))); rig.Advance(1200);
        rig.Clock.Now = Ms(1500); rig.Detector.Advance(Ms(1430), Ms(1500));
        Assert.True(rig.Detector.IsPreview); Assert.Equal(1, rig.Detector.Scale);
        rig.Input.EmitStroke(Ms(1440)); Assert.True(rig.Detector.IsPreview); Assert.Equal(0, rig.Detector.Count);
        rig.Detector.Advance(Ms(1450), Ms(1510)); Assert.False(rig.Detector.IsPreview); Assert.Equal(GesturePhase.Idle, rig.Detector.Phase); Monotonic(rig);
    }
    [Fact] public void LaterWakeAcceptsSemanticTimeBehindAnAlreadyAppliedStroke()
    {
        using var rig = Active(); rig.Advance(330); rig.Clock.Now = Ms(740); rig.Input.EmitStroke(Ms(670));
        rig.Detector.Advance(Ms(680), Ms(750));
        Assert.Equal(GesturePhase.Growing, rig.Detector.Phase); Assert.True(rig.Detector.Scale > 1); Monotonic(rig);
    }
    [Fact] public void PostClickCountingDoesNotRetainAnUnreachableOldCompletion()
    {
        using var rig = Active(); rig.Advance(700); rig.Clock.Now = Ms(1000);
        rig.Input.EmitButtons(Ms(930), PointerButtons.Left);
        rig.Input.EmitButtons(Ms(940), PointerButtons.None);
        rig.Input.EmitStroke(Ms(950));
        Assert.Equal(GesturePhase.Counting, rig.Detector.Phase); Assert.Equal(1, rig.Detector.Count);
        rig.Detector.Advance(Ms(1100), Ms(1100));
        Assert.Equal(GesturePhase.Idle, rig.Detector.Phase); Assert.True(rig.Detector.CanApplyProfile);
    }
    private static DetectorRig Active(){var rig=new DetectorRig();rig.Stroke(0);rig.Stroke(100);rig.Stroke(200);return rig;}
    private static TimeSpan Ms(double value)=>TimeSpan.FromMilliseconds(value);
    private static void Monotonic(DetectorRig rig)
    {for(int i=1;i<rig.Engine.Samples.Count;i++)Assert.True(rig.Engine.Samples[i].EvaluatedAt>=rig.Engine.Samples[i-1].EvaluatedAt);}
}
