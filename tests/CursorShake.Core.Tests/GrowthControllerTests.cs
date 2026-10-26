using CursorShake.Core.Growth;
using Xunit;

namespace CursorShake.Core.Tests;

public sealed class GrowthControllerTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(300, 3.34375)]
    [InlineData(600, 8.5)]
    [InlineData(900, 13.65625)]
    [InlineData(1200, 16)]
    [InlineData(5000, 16)]
    public void DefaultGrowthMatchesSpecifiedSmoothstepMilestones(double milliseconds, double expected)
    {
        var engine = new RecordingEngine();
        var growth = new GrowthController(engine, new GrowthProfile());
        growth.BeginGrowth(1, TimeSpan.Zero, 7);
        growth.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Assert.Equal(expected, growth.Scale, 8);
        Assert.All(engine.Samples, s => Assert.InRange(s.Scale, 1, 16));
    }

    [Fact]
    public void HoldPreservesScaleAndReturnHasConfiguredDuration()
    {
        var growth = new GrowthController(new RecordingEngine(), new GrowthProfile { ReturnDuration = TimeSpan.FromMilliseconds(400) });
        growth.BeginGrowth(1, TimeSpan.Zero, 1);
        growth.Advance(TimeSpan.FromMilliseconds(600));
        growth.Hold(TimeSpan.FromMilliseconds(600));
        growth.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.Equal(8.5, growth.Scale, 8);
        growth.Return(TimeSpan.FromMilliseconds(1000));
        growth.Advance(TimeSpan.FromMilliseconds(1200));
        Assert.Equal(4.75, growth.Scale, 8);
        growth.Advance(TimeSpan.FromMilliseconds(1400));
        Assert.Equal(1, growth.Scale);
        Assert.True(growth.IsAtNormal);
    }

    [Fact]
    public void ChangingDriveDoesNotJumpScaleOrRestartTheCurve()
    {
        var growth = new GrowthController(new RecordingEngine(), new GrowthProfile());
        growth.BeginGrowth(1, TimeSpan.Zero, 1);
        growth.Advance(TimeSpan.FromMilliseconds(300));
        var before = growth.Scale;
        growth.UpdateDrive(1.5, TimeSpan.FromMilliseconds(300));
        Assert.Equal(before, growth.Scale);
        growth.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(8.5, growth.Scale, 8);
    }

    [Fact]
    public void ResumeGrowthStartsAtCurrentScaleAndUsesRemainingDistance()
    {
        var growth = new GrowthController(new RecordingEngine(), new GrowthProfile());
        growth.BeginGrowth(1, TimeSpan.Zero, 2);
        growth.Advance(TimeSpan.FromMilliseconds(600));
        growth.Hold(TimeSpan.FromMilliseconds(600));
        growth.BeginGrowth(1, TimeSpan.FromMilliseconds(1000), 2);
        Assert.Equal(8.5, growth.Scale, 8);
        growth.Advance(TimeSpan.FromMilliseconds(1600));
        Assert.Equal(16, growth.Scale, 8);
    }
}
