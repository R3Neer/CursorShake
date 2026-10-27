namespace CursorShake.Windows.Cursors;

/// <summary>Bounds successful automatic baseline reclaims. Owned by the renderer's serial loop.</summary>
internal sealed class CursorReloadRecoveryPolicy
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private const int MaximumAttempts = 3;
    private readonly Queue<TimeSpan> attempts = new();
    private TimeSpan? lastObserved;

    /// <summary>Checks capacity without consuming it. An ineligible native snapshot must never be recorded.</summary>
    internal bool CanAttempt(TimeSpan now)
    {
        Observe(now);
        return attempts.Count(at => now - at < Window) < MaximumAttempts;
    }

    /// <summary>Records one successful native baseline reclaim after a preceding capacity check.</summary>
    internal void RecordAttempt(TimeSpan now)
    {
        Observe(now);
        while (attempts.TryPeek(out var oldest) && now - oldest >= Window) attempts.Dequeue();
        if (attempts.Count >= MaximumAttempts)
            throw new InvalidOperationException("Automatic cursor recovery is limited to three attempts per 60 seconds.");
        attempts.Enqueue(now);
    }

    private void Observe(TimeSpan now)
    {
        if (now < TimeSpan.Zero || (lastObserved is { } previous && now < previous))
            throw new ArgumentOutOfRangeException(nameof(now), "Recovery policy requires nonnegative monotonic application time.");
        lastObserved = now;
    }
}
