using CursorShake.Core.Cursors;
using CursorShake.Core.Input;
using CursorShake.Core.Runtime;
using CursorShake.Core.Shake;

namespace CursorShake.Core.Tests;

internal sealed class TestClock : IMonotonicClock
{
    public TimeSpan Now { get; set; }
}

internal sealed class TestInput : IPointerInput
{
    public event Action<ShakeStroke>? Stroke;
    public event Action<ButtonsChanged>? ButtonsChanged;
    public event Action<InputDiscontinuity>? Discontinuity;
    public PointerButtons CurrentButtons { get; private set; }
    public int ResetCount { get; private set; }
    public void Reset() => ResetCount++;
    public void EmitStroke(TimeSpan time) => Stroke?.Invoke(new(time));
    public void EmitButtons(TimeSpan time, PointerButtons buttons)
    {
        CurrentButtons = buttons;
        ButtonsChanged?.Invoke(new(time, buttons));
    }
    public void EmitDiscontinuity(TimeSpan time, PointerButtons buttons = PointerButtons.None)
    {
        CurrentButtons = buttons;
        Discontinuity?.Invoke(new(time, buttons));
    }
}

internal sealed class RecordingEngine : ICursorEngine
{
    public List<ScaleSample> Samples { get; } = [];
    public void SetScale(ScaleSample sample) => Samples.Add(sample);
}

internal sealed class DetectorRig : IDisposable
{
    public TestClock Clock { get; } = new();
    public TestInput Input { get; } = new();
    public RecordingEngine Engine { get; } = new();
    public ShakeDetector Detector { get; }
    public DetectorRig(GestureProfile? profile = null) => Detector = new(Input, Engine, Clock, profile);
    public void Advance(double milliseconds)
    {
        Clock.Now = TimeSpan.FromMilliseconds(milliseconds);
        Detector.Advance(Clock.Now);
    }
    public void Stroke(double milliseconds)
    {
        Clock.Now = TimeSpan.FromMilliseconds(milliseconds);
        Input.EmitStroke(Clock.Now);
    }
    public void Buttons(double milliseconds, PointerButtons buttons)
    {
        Clock.Now = TimeSpan.FromMilliseconds(milliseconds);
        Input.EmitButtons(Clock.Now, buttons);
    }
    public void Dispose() => Detector.Dispose();
}
