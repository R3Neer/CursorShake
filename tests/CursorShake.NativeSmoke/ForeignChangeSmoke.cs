using System.Diagnostics;
using System.Text.Json;
using CursorShake.Core.Runtime;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;

namespace CursorShake.NativeSmoke;

internal static class ForeignChangeSmoke
{
    internal static int Run(string root, string guard, string output)
    {
        Directory.CreateDirectory(root);
        var cases = new List<object>(); string? failure = null;
        try { Execute().GetAwaiter().GetResult(); } catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = failure is null, MutationAttempted = true, Failure = failure, Cases = cases }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
        async Task Execute()
        {
            foreach (bool owned in new[] { false, true })
            {
                string state = Path.Combine(root, owned ? "owned-arrow" : "untouched-engine");
                var original = CursorNative.Capture(CursorNative.LoadCursor(0, (nint)(uint)CursorRole.Arrow));
                var baseline = CursorRoles.All.ToDictionary(r => r, r => Convert.ToHexString(CursorFingerprint.Current(r)));
                string? foreignHash = null; bool testCleanup = false;
                RestoreResult? result = null; CursorEngineStatus? engineStatus = null;
                try
                {
                    if (owned)
                    {
                        using var session = new NativeCursorSession(state, guard);
                        session.Publish(CursorRole.Arrow, CrashGuardSmoke.Colored(48, 29));
                        ReplaceForeign();
                        Need(session.CheckForExternalChanges(), "Owned Arrow change must be detected.");
                        result = session.RestoreOwned();
                        VerifyPreserved();
                    }
                    else
                    {
                        using var engine = new WindowsCursorEngine(state, guard, new Clock());
                        using var changed = new ManualResetEventSlim();
                        engine.StatusChanged += status => { if (status == CursorEngineStatus.ExternalChange) changed.Set(); };
                        ReplaceForeign();
                        engine.CheckExternalChanges();
                        Need(changed.Wait(TimeSpan.FromSeconds(10)), "Engine must detect a changed untouched role.");
                        engine.SetScale(new(16, TimeSpan.FromSeconds(1), 303));
                        engine.Observe(new(CursorRole.Arrow, true, (nint)111, 32));
                        engineStatus = engine.Status;
                        Need(engineStatus == CursorEngineStatus.ExternalChange, "Observation must preserve ExternalChange.");
                        result = await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
                        VerifyPreserved();
                    }
                }
                finally
                {
                    // This is cleanup of our own foreign fixture, never product recovery or forced scheme restoration.
                    if (foreignHash is not null && Convert.ToHexString(CursorFingerprint.Current(CursorRole.Arrow)) == foreignHash)
                    {
                        using var restored = CursorNative.Create(original);
                        Need(CursorNative.SetSystemCursor(restored.Transfer(), (uint)CursorRole.Arrow), "Cannot clean up the test-owned foreign Arrow.");
                        testCleanup = Convert.ToHexString(CursorFingerprint.Current(CursorRole.Arrow)) == baseline[CursorRole.Arrow];
                    }
                }
                Need(testCleanup, "Foreign Arrow cleanup was not verified; inspect desktop before continuing.");
                Need(CursorRoles.All.All(role => Convert.ToHexString(CursorFingerprint.Current(role)) == baseline[role]), "A role differs after conditional test cleanup.");
                var pending = RecoveryBootstrap.Inspect(state);
                cases.Add(new { Owned = owned, EngineStatus = engineStatus, ProductReportedAllOwnedRestored = result?.AllOwnedRestored, ProductDiagnostics = result?.Diagnostics, ForeignPreserved = true, Other16Unchanged = true, ConditionalArrowCleanupVerified = testCleanup, BlockingReceipts = pending.Count(p => p.BlocksNewSession), RecoverySummaries = pending.Select(p => p.Summary).ToArray() });
                void ReplaceForeign()
                {
                    using var foreign = CursorNative.Create(CrashGuardSmoke.Colored(40, 211));
                    Need(CursorNative.SetSystemCursor(foreign.Transfer(), (uint)CursorRole.Arrow), "Cannot install the test-owned foreign Arrow.");
                    foreignHash = Convert.ToHexString(CursorFingerprint.Current(CursorRole.Arrow));
                }
                void VerifyPreserved()
                {
                    Need(Convert.ToHexString(CursorFingerprint.Current(CursorRole.Arrow)) == foreignHash, "Product recovery overwrote the foreign cursor.");
                    Need(CursorRoles.All.Where(r => r != CursorRole.Arrow).All(role => Convert.ToHexString(CursorFingerprint.Current(role)) == baseline[role]), "External ownership must cede all other roles too.");
                }
            }
        }
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Clock : IMonotonicClock { private readonly Stopwatch timer = Stopwatch.StartNew(); public TimeSpan Now => timer.Elapsed; }
}
