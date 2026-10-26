using CursorShake.Core.Shake;
namespace CursorShake.Core.Input;

/// <summary>Recognizes one confirmed reversal, without counting activation strokes.</summary>
public sealed class ShakeStrokeRecognizer
{
    private GestureProfile _profile;
    private PointerPosition _origin, _extreme, _direction;
    private TimeSpan _originAt, _extremeAt, _lastAt;
    private bool _hasSample, _hasDirection;
    private double _maximumProjection;

    public ShakeStrokeRecognizer(GestureProfile profile) { profile.Validate(); _profile = profile; }
    public void ApplyProfile(GestureProfile profile) { profile.Validate(); _profile = profile; Reset(); }
    public void Reset() { _hasSample = _hasDirection = false; _maximumProjection = 0; }

    public ShakeStroke? Add(PointerPosition position, TimeSpan occurredAt)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y)) throw new ArgumentException("Pointer coordinates must be finite.", nameof(position));
        if (occurredAt < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(occurredAt));
        if (!_hasSample || occurredAt < _lastAt)
        {
            Seed(position, occurredAt);
            return null;
        }
        _lastAt = occurredAt;
        var fromOrigin = Subtract(position, _origin);
        double distance = Length(fromOrigin);
        if (!_hasDirection)
        {
            if (distance < _profile.MinimumTravel) return null;
            if (!FastEnough(distance, occurredAt - _originAt)) { Seed(position, occurredAt); return null; }
            _direction = Divide(fromOrigin, distance);
            _hasDirection = true;
            _extreme = position;
            _extremeAt = occurredAt;
            _maximumProjection = distance;
            return null;
        }

        // A substantial sideways turn invalidates the stable leg; tiny turns are not summed.
        double alignment = distance > 0 ? Dot(fromOrigin, _direction) / distance : 1;
        double reverseLimit = Math.Cos(_profile.MinimumReversalAngleDegrees * Math.PI / 180);
        if (distance >= _profile.MinimumTravel && alignment < 0.5 && alignment > reverseLimit)
        {
            Seed(position, occurredAt);
            return null;
        }
        double projection = Dot(fromOrigin, _direction);
        if (projection > _maximumProjection)
        {
            _maximumProjection = projection;
            _extreme = position;
            _extremeAt = occurredAt;
            return null;
        }
        var reversal = Subtract(position, _extreme);
        double travel = Length(reversal);
        if (travel < _profile.MinimumTravel || Dot(reversal, _direction) / travel > reverseLimit + 1e-12) return null;
        if (!FastEnough(travel, occurredAt - _extremeAt)) { Seed(position, occurredAt); return null; }
        _origin = _extreme;
        _originAt = _extremeAt;
        _direction = Divide(reversal, travel);
        _extreme = position;
        _extremeAt = occurredAt;
        _maximumProjection = travel;
        return new ShakeStroke(occurredAt);
    }

    private void Seed(PointerPosition position, TimeSpan at)
    {
        _origin = _extreme = position;
        _originAt = _extremeAt = _lastAt = at;
        _hasSample = true;
        _hasDirection = false;
        _maximumProjection = 0;
    }
    private bool FastEnough(double distance, TimeSpan duration) => duration > TimeSpan.Zero && distance / duration.TotalSeconds >= _profile.MinimumSpeed;
    private static PointerPosition Subtract(PointerPosition a, PointerPosition b) => new(a.X - b.X, a.Y - b.Y);
    private static PointerPosition Divide(PointerPosition a, double divisor) => new(a.X / divisor, a.Y / divisor);
    private static double Dot(PointerPosition a, PointerPosition b) => a.X * b.X + a.Y * b.Y;
    private static double Length(PointerPosition a) => Math.Sqrt(a.X * a.X + a.Y * a.Y);
}
