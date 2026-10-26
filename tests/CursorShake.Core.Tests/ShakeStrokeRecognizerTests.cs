using CursorShake.Core.Input;
using CursorShake.Core.Shake;
using Xunit;

namespace CursorShake.Core.Tests;

public sealed class ShakeStrokeRecognizerTests
{
    [Theory]
    [InlineData(40, 0)]
    [InlineData(0, 40)]
    [InlineData(40, 40)]
    [InlineData(-40, 40)]
    public void DeliberateReversalsAreRecognizedInEveryDirection(double dx, double dy)
    {
        var recognizer = new ShakeStrokeRecognizer(new GestureProfile());
        Assert.Null(recognizer.Add(new(0, 0), TimeSpan.Zero));
        Assert.Null(recognizer.Add(new(dx, dy), TimeSpan.FromMilliseconds(100)));
        var first = recognizer.Add(new(0, 0), TimeSpan.FromMilliseconds(200));
        var second = recognizer.Add(new(dx, dy), TimeSpan.FromMilliseconds(300));
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(TimeSpan.FromMilliseconds(200), first!.OccurredAt);
        Assert.Equal(TimeSpan.FromMilliseconds(300), second!.OccurredAt);
    }

    [Fact]
    public void FirstLegAndStraightMotionDoNotCountAsShakes()
    {
        var recognizer = new ShakeStrokeRecognizer(new GestureProfile());
        for (int i = 0; i < 30; i++)
            Assert.Null(recognizer.Add(new(i * 25, 0), TimeSpan.FromMilliseconds(i * 50)));
    }

    [Fact]
    public void SmallFastTremorDoesNotAccumulateIntoAReversal()
    {
        var recognizer = new ShakeStrokeRecognizer(new GestureProfile());
        for (int i = 0; i < 200; i++)
            Assert.Null(recognizer.Add(new(i % 2 == 0 ? -3 : 3, 0), TimeSpan.FromMilliseconds(i * 10)));
    }

    [Fact]
    public void LongButSlowAlternatingLegsDoNotCount()
    {
        var recognizer = new ShakeStrokeRecognizer(new GestureProfile());
        for (int i = 0; i < 20; i++)
            Assert.Null(recognizer.Add(new(i % 2 == 0 ? 0 : 40, 0), TimeSpan.FromMilliseconds(i * 300)));
    }

    [Fact]
    public void SmoothCircleDoesNotBecomeShakesBySummingSmallTurns()
    {
        var recognizer = new ShakeStrokeRecognizer(new GestureProfile());
        for (int i = 0; i <= 144; i++)
        {
            var angle = i * Math.PI / 36;
            Assert.Null(recognizer.Add(new(80 * Math.Cos(angle), 80 * Math.Sin(angle)), TimeSpan.FromMilliseconds(i * 10)));
        }
    }

    [Fact]
    public void MinimumTravelBoundaryIsInclusive()
    {
        var recognizer = new ShakeStrokeRecognizer(new GestureProfile());
        recognizer.Add(new(0, 0), TimeSpan.Zero);
        recognizer.Add(new(32, 0), TimeSpan.FromMilliseconds(100));
        Assert.NotNull(recognizer.Add(new(16, 0), TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void ResetDiscardsThePreviousLeg()
    {
        var recognizer = new ShakeStrokeRecognizer(new GestureProfile());
        recognizer.Add(new(0, 0), TimeSpan.Zero);
        recognizer.Add(new(40, 0), TimeSpan.FromMilliseconds(100));
        recognizer.Reset();
        Assert.Null(recognizer.Add(new(0, 0), TimeSpan.FromMilliseconds(200)));
        Assert.Null(recognizer.Add(new(40, 0), TimeSpan.FromMilliseconds(300)));
        Assert.NotNull(recognizer.Add(new(0, 0), TimeSpan.FromMilliseconds(400)));
    }
}
