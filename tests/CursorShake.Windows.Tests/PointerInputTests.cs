using CursorShake.Core.Input;
using CursorShake.Core.Shake;
using CursorShake.Windows.Input;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class PointerInputTests
{
    [Theory]
    [InlineData(RawPointerKind.LeftDown, RawPointerKind.LeftUp, PointerButtons.Left)]
    [InlineData(RawPointerKind.MiddleDown, RawPointerKind.MiddleUp, PointerButtons.Middle)]
    [InlineData(RawPointerKind.RightDown, RawPointerKind.RightUp, PointerButtons.Right)]
    public void ButtonTranslationResetsOldGeometryBeforeRecognizingNewStrokes(RawPointerKind down, RawPointerKind up, PointerButtons button)
    {
        PointerButtons held = PointerButtons.None;
        var input = new PointerInput(new GestureProfile(), () => held, (_, _) => (1, 96));
        var strokes = new List<ShakeStroke>();
        var buttons = new List<ButtonsChanged>();
        input.Stroke += strokes.Add; input.ButtonsChanged += buttons.Add;
        Move(input, 0, 0); Move(input, 40, 100);
        held = button; input.Process(new(down, 40, 0), TimeSpan.FromMilliseconds(150));
        Move(input, 0, 200);
        held = PointerButtons.None; input.Process(new(up, 0, 0), TimeSpan.FromMilliseconds(250));
        Move(input, 40, 300); Move(input, 0, 400);
        Assert.Empty(strokes);
        Move(input, 40, 500);
        Assert.Equal(TimeSpan.FromMilliseconds(500), Assert.Single(strokes).OccurredAt);
        Assert.Equal(button, buttons[0].Buttons);
        Assert.Equal(PointerButtons.None, buttons[1].Buttons);
    }

    [Theory]
    [InlineData(96, 16)]
    [InlineData(192, 32)]
    public void PhysicalMovementIsNormalizedToLogicalTravel(double dpi, int fullLeg)
    {
        var input = new PointerInput(new GestureProfile { MinimumSpeed = 0 }, () => PointerButtons.None, (_, _) => (1, dpi));
        var strokes = new List<ShakeStroke>(); input.Stroke += strokes.Add;
        Move(input, 0, 0); Move(input, fullLeg, 100); Move(input, fullLeg / 2, 150);
        Assert.Empty(strokes);
        Move(input, 0, 200);
        Assert.Equal(TimeSpan.FromMilliseconds(200), Assert.Single(strokes).OccurredAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MonitorOrDpiBoundaryDiscardsTheOldLegInsteadOfInventingAReversal(bool changeMonitor)
    {
        (nint Monitor, double Dpi) context = (1, 96);
        var input = new PointerInput(new GestureProfile(), () => PointerButtons.None, (_, _) => context);
        int discontinuities = 0;
        var strokes = new List<ShakeStroke>();
        input.Stroke += strokes.Add; input.Discontinuity += _ => discontinuities++;
        Move(input, 0, 0); Move(input, 40, 100);
        context = changeMonitor ? (2, 192) : (1, 192);
        Move(input, -1000, 200);
        Assert.Empty(strokes);
        Assert.Equal(1, discontinuities);
        Move(input, -920, 300);
        Assert.Empty(strokes);
        Move(input, -1000, 400);
        Assert.Single(strokes);
    }

    [Fact]
    public void DiscontinuityResynchronizesThePhysicalButtonSnapshot()
    {
        PointerButtons held = PointerButtons.None;
        var input = new PointerInput(new GestureProfile(), () => held, (_, _) => (1, 96));
        InputDiscontinuity? observed = null; input.Discontinuity += value => observed = value;
        Move(input, 0, 0); Move(input, 40, 100);
        held = PointerButtons.Left | PointerButtons.Right;
        input.SignalDiscontinuity(TimeSpan.FromMilliseconds(150));
        Assert.NotNull(observed);
        Assert.Equal(held, observed!.Buttons);
        Assert.Equal(held, input.CurrentButtons);
    }

    [Fact]
    public void GeometryResetDoesNotReplaceAdmittedButtonStateWithAnOlderPhysicalSnapshot()
    {
        PointerButtons physicalSnapshot = PointerButtons.None;
        var input = new PointerInput(new GestureProfile(), () => physicalSnapshot, (_, _) => (1, 96));
        var clock = new RuntimeTestClock();
        using var detector = new ShakeDetector(input, new RuntimeTestEngine(), clock);
        input.Process(new(RawPointerKind.LeftDown, 0, 0), TimeSpan.Zero);
        Assert.Equal(PointerButtons.Left, input.CurrentButtons);
        physicalSnapshot = PointerButtons.Left;
        clock.Now = TimeSpan.FromMilliseconds(10);
        input.Process(new(RawPointerKind.RightDown, 0, 0), clock.Now);
        Assert.Equal(PointerButtons.Left | PointerButtons.Right, input.CurrentButtons);
        physicalSnapshot = PointerButtons.Right;
        clock.Now = TimeSpan.FromMilliseconds(20);
        input.Process(new(RawPointerKind.LeftUp, 0, 0), clock.Now);
        Assert.Equal(PointerButtons.Right, input.CurrentButtons);
        Assert.Equal(GesturePhase.Blocked, detector.Phase);
        for (int i = 0; i < 8; i++)
        {
            clock.Now = TimeSpan.FromMilliseconds(100 + i * 100);
            input.Process(new(RawPointerKind.Move, i % 2 == 0 ? 0 : 40, 0), clock.Now);
        }
        Assert.Equal(0, detector.Count);
        Assert.Equal(1, detector.Scale);
        Assert.Equal(GesturePhase.Blocked, detector.Phase);
    }

    private static void Move(PointerInput input, int x, int milliseconds) => input.Process(new(RawPointerKind.Move, x, 0), TimeSpan.FromMilliseconds(milliseconds));
}
