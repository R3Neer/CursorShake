namespace CursorShake.Core.Growth;

public sealed record GrowthProfile
{
    public double MaximumScale { get; init; } = 16;
    public TimeSpan GrowthDuration { get; init; } = TimeSpan.FromMilliseconds(1200);
    public TimeSpan InactivityDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan ReturnDuration { get; init; } = TimeSpan.FromMilliseconds(250);
    public double MaximumCadenceMultiplier { get; init; } = 1.5;

    public void Validate()
    {
        if (!double.IsFinite(MaximumScale) || MaximumScale < 1) throw new ArgumentOutOfRangeException(nameof(MaximumScale));
        if (GrowthDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(GrowthDuration));
        if (InactivityDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(InactivityDelay));
        if (ReturnDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ReturnDuration));
        if (!double.IsFinite(MaximumCadenceMultiplier) || MaximumCadenceMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(MaximumCadenceMultiplier));
    }
}
