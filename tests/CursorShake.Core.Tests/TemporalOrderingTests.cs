using CursorShake.Core.Input;
using Xunit;

namespace CursorShake.Core.Tests;

public sealed class TemporalOrderingTests
{
    [Fact]
    public void AdmittedStrokeCadenceUsesOccurrenceNotCallbackProcessingDelay()
    {
        using var rig = new DetectorRig();
        rig.Input.EmitStroke(TimeSpan.Zero);
        rig.Clock.Now = TimeSpan.FromMilliseconds(150);
        rig.Input.EmitStroke(TimeSpan.FromMilliseconds(100));
        rig.Clock.Now = TimeSpan.FromMilliseconds(250);
        rig.Input.EmitStroke(TimeSpan.FromMilliseconds(200));
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
        rig.Advance(330);
        Assert.True(rig.Detector.Scale > 1);
        Assert.Equal("Holding", rig.Detector.Phase.ToString());
        for (int i = 1; i < rig.Engine.Samples.Count; i++)
            Assert.True(rig.Engine.Samples[i].EvaluatedAt >= rig.Engine.Samples[i - 1].EvaluatedAt);
    }

    [Fact]
    public void ValidStrokeAtInactivityBoundaryWinsOverStartingReturn()
    {
        using var rig = new DetectorRig();
        rig.Stroke(0); rig.Stroke(100); rig.Stroke(200);
        rig.Stroke(700);
        var scale = rig.Detector.Scale;
        Assert.True(scale > 1);
        Assert.Equal("Growing", rig.Detector.Phase.ToString());
        rig.Advance(750);
        Assert.True(rig.Detector.Scale > scale);
    }

    [Fact]
    public void ButtonDownAtTheSameInstantBlocksFurtherStrokeAndReturnsImmediately()
    {
        using var rig = new DetectorRig();
        rig.Stroke(0); rig.Stroke(100); rig.Stroke(200); rig.Advance(300);
        var scale = rig.Detector.Scale;
        rig.Buttons(300, PointerButtons.Left);
        rig.Stroke(300);
        rig.Advance(425);
        Assert.Equal(0, rig.Detector.Count);
        Assert.InRange(rig.Detector.Scale, 1.000001, scale - 0.000001);
    }

    [Fact]
    public void OutOfOrderStrokeResetsRatherThanRestoringAnEarlierCount()
    {
        using var rig = new DetectorRig();
        rig.Stroke(0); rig.Stroke(100);
        rig.Clock.Now = TimeSpan.FromMilliseconds(150);
        rig.Input.EmitStroke(TimeSpan.FromMilliseconds(50));
        Assert.Equal(0, rig.Detector.Count);
        rig.Stroke(200);
        Assert.Equal(1, rig.Detector.Count);
    }
}
