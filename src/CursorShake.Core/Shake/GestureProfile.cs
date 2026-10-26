using CursorShake.Core.Growth;
namespace CursorShake.Core.Shake;

public sealed record GestureProfile
{
    public int CountToActivate { get; init; } = 3;
    public TimeSpan MaximumInterval { get; init; } = TimeSpan.FromMilliseconds(130);
    public double MinimumTravel { get; init; } = 16;
    public double MinimumSpeed { get; init; } = 200;
    public double MinimumReversalAngleDegrees { get; init; } = 120;
    public GrowthProfile Growth { get; init; } = new();

    public void Validate()
    {
        if (CountToActivate is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(CountToActivate));
        if (MaximumInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MaximumInterval));
        if (!double.IsFinite(MinimumTravel) || MinimumTravel <= 0) throw new ArgumentOutOfRangeException(nameof(MinimumTravel));
        if (!double.IsFinite(MinimumSpeed) || MinimumSpeed < 0) throw new ArgumentOutOfRangeException(nameof(MinimumSpeed));
        if (!double.IsFinite(MinimumReversalAngleDegrees) || MinimumReversalAngleDegrees is < 90 or > 180) throw new ArgumentOutOfRangeException(nameof(MinimumReversalAngleDegrees));
        ArgumentNullException.ThrowIfNull(Growth);
        Growth.Validate();
        if (Growth.InactivityDelay < MaximumInterval) throw new ArgumentException("The inactivity delay must include the complete maximum stroke interval.", nameof(Growth));
    }
}
