using System.Diagnostics;
using CursorShake.Core.Runtime;
namespace CursorShake.Windows.Input;
public sealed class StopwatchClock : IMonotonicClock
{
    private readonly long origin=Stopwatch.GetTimestamp();
    public TimeSpan Now=>Stopwatch.GetElapsedTime(origin);
}
