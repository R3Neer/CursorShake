using CursorShake.Core.Cursors;
namespace CursorShake.Core.Growth;

/// <summary>Advances one scale curve. Activation and inactivity policy belong to ShakeDetector.</summary>
public sealed class GrowthController
{
    private enum Curve { Holding, Growing, Returning }
    private readonly ICursorEngine _engine;
    private GrowthProfile _profile;
    private Curve _curve;
    private TimeSpan _lastTime;
    private double _from = 1, _target = 1, _progress, _durationSeconds = 1, _drive = 1;
    private long _epoch;
    private ScaleSample? _lastPublished;
    public GrowthController(ICursorEngine engine, GrowthProfile profile)
    {
        ArgumentNullException.ThrowIfNull(engine); profile.Validate(); _engine = engine; _profile = profile;
    }
    public double Scale { get; private set; } = 1;
    public bool IsAtNormal => Scale <= 1;
    public bool IsAnimating => _curve != Curve.Holding;
    public bool IsGrowing => _curve == Curve.Growing;
    public bool IsReturning => _curve == Curve.Returning;
    public TimeSpan? CompletionAt => IsAnimating ? _lastTime + TimeSpan.FromTicks(Math.Max(1, (long)Math.Ceiling((1 - _progress) * _durationSeconds / _drive * TimeSpan.TicksPerSecond))) : null;
    public TimeSpan? NextDeadline => CompletionAt is { } end ? Min(_lastTime + TimeSpan.FromSeconds(1.0 / 60), end) : null;

    public void BeginGrowth(double drive, TimeSpan now, long gestureEpoch)
    {
        Advance(now); _epoch = gestureEpoch; _drive = ValidDrive(drive);
        _from = Scale; _target = _profile.MaximumScale; _progress = 0;
        _durationSeconds = _profile.GrowthDuration.TotalSeconds * (_profile.MaximumScale == 1 ? 0 : (_target - _from) / (_profile.MaximumScale - 1));
        _curve = _durationSeconds > 0 && _target > _from ? Curve.Growing : Curve.Holding;
        Publish(now);
    }
    public void UpdateDrive(double drive, TimeSpan now) { Advance(now); _drive = ValidDrive(drive); }
    public void Hold(TimeSpan now) { Advance(now); _curve = Curve.Holding; }
    public void Return(TimeSpan now)
    {
        Advance(now);
        if (_curve == Curve.Returning) return;
        _from = Scale; _target = 1; _progress = 0; _durationSeconds = _profile.ReturnDuration.TotalSeconds; _drive = 1;
        _curve = Scale > 1 ? Curve.Returning : Curve.Holding;
    }
    /// <summary>Discards an unavailable renderer's curve without publishing any intermediate scale.</summary>
    public void ResetToNormal(TimeSpan now, long gestureEpoch)
    {
        if (now < _lastTime) throw new ArgumentOutOfRangeException(nameof(now));
        _lastTime = now; _epoch = gestureEpoch; _curve = Curve.Holding;
        _from = _target = Scale = 1; _progress = 0;
        Publish(now);
    }
    public void SetEpoch(long epoch) => _epoch = epoch;
    public void ApplyProfile(GrowthProfile profile, TimeSpan now)
    {
        profile.Validate(); Advance(now);
        if (!IsAtNormal || IsAnimating) throw new InvalidOperationException("A growth profile can only change at normal idle size.");
        _profile = profile;
    }
    public void Advance(TimeSpan now)
    {
        if (now < _lastTime) throw new ArgumentOutOfRangeException(nameof(now), "Curve time cannot move backwards.");
        if (IsAnimating)
        {
            _progress = Math.Clamp(_progress + (now - _lastTime).TotalSeconds * _drive / _durationSeconds, 0, 1);
            if (_progress >= 1 - 1e-12) _progress = 1;
            double eased = _progress * _progress * (3 - 2 * _progress);
            Scale = Math.Clamp(_from + (_target - _from) * eased, 1, _profile.MaximumScale);
            if (_progress >= 1) { Scale = _target; _curve = Curve.Holding; }
        }
        _lastTime = now;
        Publish(now);
    }
    private double ValidDrive(double drive)
    {
        if (!double.IsFinite(drive) || drive < 1) throw new ArgumentOutOfRangeException(nameof(drive));
        return Math.Min(drive, _profile.MaximumCadenceMultiplier);
    }
    private void Publish(TimeSpan now)
    {
        var sample = new ScaleSample(Scale, now, _epoch);
        if (_lastPublished is not { } last || last.Scale != Scale || last.GestureEpoch != _epoch)
        { _engine.SetScale(sample); _lastPublished = sample; }
    }
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;
}
