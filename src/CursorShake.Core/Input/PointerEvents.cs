namespace CursorShake.Core.Input;

[Flags]
public enum PointerButtons { None = 0, Left = 1, Middle = 2, Right = 4 }

public readonly record struct PointerPosition(double X, double Y);
public sealed record ShakeStroke(TimeSpan OccurredAt);
public sealed record ButtonsChanged(TimeSpan OccurredAt, PointerButtons Buttons);
public sealed record InputDiscontinuity(TimeSpan OccurredAt, PointerButtons Buttons);

public interface IPointerInput
{
    event Action<ShakeStroke>? Stroke;
    event Action<ButtonsChanged>? ButtonsChanged;
    event Action<InputDiscontinuity>? Discontinuity;
    PointerButtons CurrentButtons { get; }
    void Reset();
}
