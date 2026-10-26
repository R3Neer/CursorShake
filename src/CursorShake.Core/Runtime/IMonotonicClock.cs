namespace CursorShake.Core.Runtime;

/// <summary>Elapsed time on a single monotonic origin shared by input and domain.</summary>
public interface IMonotonicClock { TimeSpan Now { get; } }
