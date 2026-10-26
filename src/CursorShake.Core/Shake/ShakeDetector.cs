using CursorShake.Core.Cursors;
using CursorShake.Core.Growth;
using CursorShake.Core.Input;
using CursorShake.Core.Runtime;

namespace CursorShake.Core.Shake;

public enum GesturePhase { Idle, Counting, Growing, Holding, Returning, Blocked }

/// <summary>Principal gesture controller. All entry points run on the same serial runtime loop.</summary>
public sealed class ShakeDetector : IDisposable
{
    private readonly IPointerInput _input;
    private readonly IMonotonicClock _clock;
    private readonly GrowthController _growth;
    private GestureProfile _profile;
    private PointerButtons _buttons;
    private TimeSpan? _lastStroke, _lastOccurrence;
    private readonly Queue<TimeSpan> _intervals = new(2);
    private TimeSpan _lastApplyAt, _semanticThrough;
    private TimeSpan? _phaseCompletion;
    private bool _fullCountRequired = true, _disposed;

    public ShakeDetector(IPointerInput input, ICursorEngine engine, IMonotonicClock clock, GestureProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(clock);
        _profile = profile ?? new GestureProfile(); _profile.Validate();
        _input = input; _clock = clock; _growth = new GrowthController(engine, _profile.Growth);
        _buttons = input.CurrentButtons & AllButtons;
        Phase = _buttons == PointerButtons.None ? GesturePhase.Idle : GesturePhase.Blocked;
        input.Stroke += OnStroke;
        input.ButtonsChanged += OnButtonsChanged;
        input.Discontinuity += OnDiscontinuity;
    }

    private const PointerButtons AllButtons = PointerButtons.Left | PointerButtons.Middle | PointerButtons.Right;
    public event Action? StateChanged;
    public GesturePhase Phase { get; private set; }
    public double Scale => _growth.Scale;
    public int Count { get; private set; }
    public bool IsPreview { get; private set; }
    public bool IsSuspended { get; private set; }
    public GestureProfile Profile => _profile;
    public long GestureEpoch { get; private set; }
    public bool CanApplyProfile => _growth.IsAtNormal && !_growth.IsAnimating && !IsPreview && _phaseCompletion is null;
    public TimeSpan? NextDeadline => Earlier(SemanticDeadline(), _growth.NextDeadline);

    public void Advance(TimeSpan now) => Advance(now, now);

    /// <summary>Only admitted events through occurredThrough authorize phase transitions. Curves are
    /// evaluated at applyAt, which may be later after a delayed runtime wake; publication never rewinds.</summary>
    public void Advance(TimeSpan occurredThrough, TimeSpan applyAt)
    {
        EnsureUsable();
        if (occurredThrough < _semanticThrough || occurredThrough < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(occurredThrough));
        if (applyAt < occurredThrough) throw new ArgumentOutOfRangeException(nameof(applyAt));
        ProcessDeadlines(occurredThrough, true);
        _growth.Advance(ApplyTime(applyAt));
        StateChanged?.Invoke();
    }

    public void Reset(TimeSpan now)
    {
        EnsureUsable();
        now = ApplyTime(now);
        _input.Reset();
        _buttons = _input.CurrentButtons & AllButtons;
        ClearSequence();
        _lastOccurrence = null;
        _fullCountRequired = true;
        IsPreview = false;
        GestureEpoch++;
        _growth.SetEpoch(GestureEpoch);
        BeginReturn(now);
        Phase = _buttons != PointerButtons.None ? GesturePhase.Blocked : GesturePhase.Returning;
        if (_phaseCompletion <= _semanticThrough) CompleteReturn();
        StateChanged?.Invoke();
    }

    public void SetSuspended(bool suspended, TimeSpan now)
    {
        EnsureUsable();
        if (IsSuspended == suspended) return;
        IsSuspended = suspended;
        Reset(now);
    }

    public void ApplyProfile(GestureProfile profile, TimeSpan now)
    {
        EnsureUsable(); profile.Validate();
        Advance(now);
        if (!CanApplyProfile) throw new InvalidOperationException("Configuration waits for normal size before becoming effective.");
        _profile = profile;
        _growth.ApplyProfile(profile.Growth, ApplyTime(now));
        Reset(now);
    }

    public bool TryStartPreview(TimeSpan now)
    {
        EnsureUsable();
        Advance(now);
        if (IsSuspended || Phase != GesturePhase.Idle || _buttons != PointerButtons.None || !CanApplyProfile) return false;
        ClearSequence(); _input.Reset();
        GestureEpoch++;
        IsPreview = true;
        _growth.BeginGrowth(1, ApplyTime(now), GestureEpoch);
        _phaseCompletion = _growth.CompletionAt;
        Phase = _growth.IsAnimating ? GesturePhase.Growing : GesturePhase.Idle;
        if (!_growth.IsAnimating) IsPreview = false;
        StateChanged?.Invoke();
        return true;
    }

    private void OnStroke(ShakeStroke stroke)
    {
        if (_disposed) return;
        var clockNow = _clock.Now;
        var now = clockNow > stroke.OccurredAt ? clockNow : stroke.OccurredAt;
        if (!AcceptOccurrence(stroke.OccurredAt, now)) return;
        ProcessDeadlines(stroke.OccurredAt, false);
        now = ApplyTime(now);
        bool canResume = !_fullCountRequired && Phase is GesturePhase.Growing or GesturePhase.Holding or GesturePhase.Returning;
        _growth.Advance(now);
        if (IsSuspended || IsPreview || _buttons != PointerButtons.None) return;
        if (_lastStroke is { } previous)
        {
            var interval = stroke.OccurredAt - previous;
            if (interval <= TimeSpan.Zero) return;
            if (interval > _profile.MaximumInterval) { Count = 0; _intervals.Clear(); }
            else { if (_intervals.Count == 2) _intervals.Dequeue(); _intervals.Enqueue(interval); }
        }
        _lastStroke = stroke.OccurredAt;
        bool active = canResume;
        if (!active)
        {
            // A post-click sequence is already fully rearmed. Its independent count supersedes
            // the previous return's logical completion, while that physical curve may still finish.
            _phaseCompletion = null;
            Count++;
        }
        if (active || Count >= _profile.CountToActivate)
        {
            double drive = CurrentDrive();
            if (Phase == GesturePhase.Growing && _growth.IsGrowing) _growth.UpdateDrive(drive, now);
            else
            {
                if (_fullCountRequired) GestureEpoch++;
                _growth.BeginGrowth(drive, now, GestureEpoch);
            }
            _fullCountRequired = false;
            _phaseCompletion = null;
            Phase = GesturePhase.Growing;
        }
        else Phase = GesturePhase.Counting;
        StateChanged?.Invoke();
    }

    private void OnButtonsChanged(ButtonsChanged changed)
    {
        if (_disposed) return;
        var clockNow = _clock.Now;
        var now = clockNow > changed.OccurredAt ? clockNow : changed.OccurredAt;
        ProcessDeadlines(changed.OccurredAt, false);
        now = ApplyTime(now);
        var next = changed.Buttons & AllButtons;
        bool pressed = (next & ~_buttons) != 0;
        _buttons = next;
        _lastOccurrence = changed.OccurredAt;
        if (pressed)
        {
            _input.Reset(); ClearSequence(); _fullCountRequired = true; IsPreview = false;
            GestureEpoch++; _growth.SetEpoch(GestureEpoch); BeginReturn(now);
        }
        else _growth.Advance(now);
        if (_buttons != PointerButtons.None) Phase = GesturePhase.Blocked;
        else if (Phase == GesturePhase.Blocked) Phase = _phaseCompletion is not null ? GesturePhase.Returning : GesturePhase.Idle;
        StateChanged?.Invoke();
    }

    private void OnDiscontinuity(InputDiscontinuity discontinuity)
    {
        if (_disposed) return;
        Reset(ApplyTime(_clock.Now > discontinuity.OccurredAt ? _clock.Now : discontinuity.OccurredAt));
        _buttons = discontinuity.Buttons & AllButtons;
        Phase = _buttons != PointerButtons.None ? GesturePhase.Blocked : _phaseCompletion is not null ? GesturePhase.Returning : GesturePhase.Idle;
    }

    private bool AcceptOccurrence(TimeSpan occurredAt, TimeSpan now)
    {
        if (occurredAt < TimeSpan.Zero || (_lastOccurrence is { } previous && occurredAt < previous))
        { Reset(now); return false; }
        _lastOccurrence = occurredAt;
        return true;
    }

    private double CurrentDrive()
    {
        if (_intervals.Count == 0) return 1;
        double mean = _intervals.Average(x => x.TotalSeconds);
        return mean > 0 ? Math.Clamp(_profile.MaximumInterval.TotalSeconds / mean, 1, _profile.Growth.MaximumCadenceMultiplier) : 1;
    }

    private TimeSpan? SemanticDeadline()
    {
        TimeSpan? next = null;
        if (!IsPreview && _lastStroke is { } last)
        {
            if (Phase == GesturePhase.Counting) next = last + _profile.MaximumInterval + TimeSpan.FromTicks(1);
            if (Phase == GesturePhase.Growing) next = last + _profile.MaximumInterval;
            if (Phase is GesturePhase.Growing or GesturePhase.Holding) next = Earlier(next, last + _profile.Growth.InactivityDelay);
        }
        if (Phase is GesturePhase.Returning or GesturePhase.Blocked || IsPreview) next = Earlier(next, _phaseCompletion);
        return next;
    }

    private void ProcessDeadlines(TimeSpan through, bool inclusive)
    {
        if (through > _semanticThrough) _semanticThrough = through;
        // Logical completion survives a physical curve evaluation beyond its end. A queued stroke
        // preceding that completion retains one-stroke resumption even if its applied scale is now 1.
        for (int i = 0; i < 12; i++)
        {
            if (SemanticDeadline() is not { } due) return;
            bool finishesReturn = Phase is GesturePhase.Returning or GesturePhase.Blocked && _phaseCompletion == due;
            if (due > through || (!inclusive && due == through && !finishesReturn)) return;
            var applyAt = ApplyTime(due);
            _growth.Advance(applyAt);
            if (Phase is GesturePhase.Returning or GesturePhase.Blocked && _phaseCompletion is { } completed && due >= completed)
            {
                CompleteReturn();
            }
            else if (IsPreview)
            {
                BeginReturn(applyAt); Phase = GesturePhase.Returning;
            }
            else if (Phase == GesturePhase.Counting)
            {
                Count = 0; _lastStroke = null; _intervals.Clear(); Phase = GesturePhase.Idle;
            }
            else if (Phase is GesturePhase.Growing or GesturePhase.Holding && _lastStroke is { } last && due >= last + _profile.Growth.InactivityDelay)
            {
                BeginReturn(applyAt); Count = 0; Phase = GesturePhase.Returning;
            }
            else if (Phase == GesturePhase.Growing) { _growth.Hold(applyAt); Phase = GesturePhase.Holding; }
            else return;
        }
        throw new InvalidOperationException("A gesture deadline failed to advance.");
    }

    private void BeginReturn(TimeSpan applyAt)
    {
        _growth.Return(applyAt);
        _phaseCompletion = _growth.CompletionAt ?? applyAt;
    }
    private void CompleteReturn()
    {
        bool wasPreview = IsPreview;
        ClearSequence(); _fullCountRequired = true; IsPreview = false; _phaseCompletion = null;
        Phase = _buttons == PointerButtons.None ? GesturePhase.Idle : GesturePhase.Blocked;
        if (wasPreview) _input.Reset();
    }
    private void ClearSequence() { Count = 0; _lastStroke = null; _intervals.Clear(); }
    private TimeSpan ApplyTime(TimeSpan requested)
    {
        if (requested < TimeSpan.Zero) requested = TimeSpan.Zero;
        if (requested > _lastApplyAt) _lastApplyAt = requested;
        return _lastApplyAt;
    }
    private static TimeSpan? Earlier(TimeSpan? a, TimeSpan? b) => a is null ? b : b is null ? a : a < b ? a : b;
    private void EnsureUsable() { ObjectDisposedException.ThrowIf(_disposed, this); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _input.Stroke -= OnStroke; _input.ButtonsChanged -= OnButtonsChanged; _input.Discontinuity -= OnDiscontinuity;
    }
}
