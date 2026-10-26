using System.Diagnostics;
using System.Text.Json;
using CursorShake.Core.Runtime;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;

namespace CursorShake.NativeSmoke;

// Exclusive native QA only; deliberately not discovered by xUnit. Never run with the user's app active.
internal static class EngineFailureRegressionSmoke
{
    internal static int Run(string root, string bundled, string guard, string output)
    {
        var cases = new List<object>(); string? failure = null; bool restored = false;
        try { Execute().GetAwaiter().GetResult(); restored = true; }
        catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = failure is null && restored, Restored = restored,
            MutationAttempted = true, Failure = failure, Cases = cases }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null && restored ? 0 : 1;

        async Task Execute()
        {
            if (Directory.Exists(root)) throw new IOException("A fresh QA root is required: " + root);
            Directory.CreateDirectory(root);
            // Preserve original ANI bytes as well as pixels before touching any shared role.
            string backup = Path.Combine(root, Guid.NewGuid().ToString("N"));
            RecoveryManifest manifest;
            using (var construction = new RecoveryConstruction(backup))
            { manifest = BaselineStore.Capture(backup, construction, null, out _, out _); construction.Complete(); }
            var baseline = manifest.Cursors.ToDictionary(c => c.Role, c => c.Fingerprint);
            await FailedApply();
            Need(MatchesBaseline(), "Failed-apply fixture changed a system role.");
            await ExternalDuringPublish();
            Need(MatchesBaseline(), "The17 baseline fingerprints were not restored.");
            // Backup and ceded receipts intentionally remain as QA evidence; never delete unverified state.

            async Task FailedApply()
            {
                using var engine = new WindowsCursorEngine(Path.Combine(root, "failed-apply"), guard, new Clock());
                using var candidate = Prepare(); int errorEvents = 0;
                engine.Error += _ => Interlocked.Increment(ref errorEvents);
                engine.BeforeThemePublish = () => throw new InvalidOperationException("QA apply failure");
                var result = await engine.ApplyThemeAsync(candidate, 16).WaitAsync(TimeSpan.FromSeconds(15));
                Need(!result.Applied && result.Error == "QA apply failure", "Caller must receive the failed apply result.");
                Need(Volatile.Read(ref errorEvents) == 0, "Failed apply must not also emit a duplicate Error event.");
                var stop = await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Need(stop.AllOwnedRestored, "Untouched failed-apply session did not stop cleanly.");
                cases.Add(new { Case = "ApplyFailureSingleDiagnostic", result.Applied, result.Error, ErrorEvents = errorEvents, StopRestored = stop.AllOwnedRestored });
            }
            async Task ExternalDuringPublish()
            {
                var expectedQa = new Dictionary<CursorRole, string>();
                bool foreignInstalled = false; bool cleanupVerified = false;
                var clock = new Clock();
                using var engine = new WindowsCursorEngine(Path.Combine(root, "external-publish"), guard, clock);
                using var external = new ManualResetEventSlim();
                int errorEvents = 0, hookCalls = 0; long publishedAtCession = -1;
                engine.Error += _ => Interlocked.Increment(ref errorEvents);
                engine.StatusChanged += value => { if (value == CursorEngineStatus.ExternalChange) external.Set(); };
                try
                {
                    var applied = await engine.ApplyThemeAsync(Prepare(), 16).WaitAsync(TimeSpan.FromSeconds(20));
                    Need(applied.Applied, "Initial Bibata theme failed: " + applied.Error);
                    // Hide the observed role to quiesce the renderer before snapshotting test-owned fingerprints.
                    engine.Observe(new(CursorRole.Arrow, false, (nint)111, 32));
                    // A rejected pre-validation command is a FIFO barrier and performs no publication.
                    using (var barrierTheme = Prepare())
                        Need(!(await engine.ApplyThemeAsync(barrierTheme, double.NaN)).Applied, "Invalid-scale barrier must reject.");
                    foreach (var role in CursorRoles.All) expectedQa[role] = Hash(role);
                    engine.BeforeNativePublish = role =>
                    {
                        if (role != CursorRole.Arrow || Interlocked.Increment(ref hookCalls) != 1) return;
                        using var replacement = CursorNative.Create(CrashGuardSmoke.Colored(41, 223));
                        Need(CursorNative.SetSystemCursor(replacement.Transfer(), (uint)CursorRole.Arrow), "Cannot publish the test-owned foreign Arrow.");
                        expectedQa[CursorRole.Arrow] = Hash(CursorRole.Arrow); foreignInstalled = true;
                    };
                    engine.Observe(new(CursorRole.Arrow, true, (nint)111, 32));
                    engine.SetScale(new(2, clock.Now, 91));
                    Need(external.Wait(TimeSpan.FromSeconds(15)), "External change during native Publish must reach ExternalChange.");
                    publishedAtCession = engine.PublishedFrames;
                    Need(engine.Status == CursorEngineStatus.ExternalChange, "Publish failure incorrectly became Degraded.");
                    engine.BeforeNativePublish = null;
                    engine.Normalize(); engine.SetScale(new(3, clock.Now, 92));
                    engine.Observe(new(CursorRole.Text, true, (nint)222, 48));
                    using var retryTheme = Prepare();
                    var retry = await engine.ApplyThemeAsync(retryTheme, 16).WaitAsync(TimeSpan.FromSeconds(15));
                    Need(!retry.Applied, "An external scheme must never be automatically adopted by theme reapplication.");
                    Need(engine.Status == CursorEngineStatus.ExternalChange, "ExternalChange must survive observation and retry.");
                    Need(engine.PublishedFrames == publishedAtCession, "A role was published after ownership was ceded.");
                    Need(errorEvents == 0, "External ownership loss is not a renderer Error.");
                    Need(CursorRoles.All.All(role => Hash(role) == expectedQa[role]), "Product changed a role after cession.");
                    var stopped = await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    Need(CursorRoles.All.All(role => Hash(role) == expectedQa[role]), "Stop overwrote the ceded scheme.");
                    cases.Add(new { Case = "ExternalChangeDuringPublish", publishedAtCession, FinalPublications = engine.PublishedFrames,
                        RetryApplied = retry.Applied, ErrorEvents = errorEvents, ProductRestore = stopped.AllOwnedRestored, stopped.Diagnostics });
                }
                finally
                {
                    engine.BeforeNativePublish = null;
                    await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    if (foreignInstalled)
                    {
                        // Test compensation only: refuse the entire cleanup if another writer changed any role.
                        Need(CursorRoles.All.All(role => Hash(role) == expectedQa[role]), "QA scheme changed externally; backups preserved and no forced cleanup attempted.");
                        foreach (var entry in manifest.Cursors)
                        {
                            Need(Hash(entry.Role) == expectedQa[entry.Role], "Role changed immediately before test compensation: " + entry.Role);
                            var pixels = BaselineStore.ReadPixels(Path.Combine(backup, entry.PixelFile));
                            using var source = entry.SourceFile is null ? CursorNative.Create(pixels) : OriginalSource(entry, pixels);
                            Need(CursorNative.SetSystemCursor(source.Transfer(), (uint)entry.Role), "Cannot restore QA-owned role: " + entry.Role);
                            Need(Hash(entry.Role) == baseline[entry.Role], "Baseline verification failed: " + entry.Role);
                        }
                        cleanupVerified = MatchesBaseline();
                        Need(cleanupVerified, "Conditional QA compensation incomplete.");
                    }
                    else Need(MatchesBaseline(), "Unexpected failure before foreign fixture left a changed scheme.");
                    cases.Add(new { Case = "ConditionalTestCompensation", ForeignInstalled = foreignInstalled, All17Restored = cleanupVerified || MatchesBaseline(), Backup = backup });
                }
            }
            PreparedTheme Prepare() => new ThemePreparer(bundled).Prepare(new AppConfiguration { Theme = "bibata-ice" }, root);
            bool MatchesBaseline() => CursorRoles.All.All(role => Hash(role) == baseline[role]);
            SafeCursorHandle OriginalSource(BaselineCursor entry, CursorPixels pixels)
            {
                string path = Path.Combine(backup, entry.SourceFile!);
                using var file = ProtectedFile.Open(path);
                Need(file.Hash() == entry.SourceHash, "Original ANI/CUR bytes changed: " + path);
                var loaded = CursorNative.LoadFile(path, pixels.Width, pixels.Height);
                if (CursorFingerprint.Of(CursorNative.Capture(loaded.DangerousGetHandle())) == entry.Fingerprint) return loaded;
                loaded.Dispose(); throw new InvalidDataException("Original ANI/CUR no longer matches: " + entry.Role);
            }
        }
    }
    private static string Hash(CursorRole role) => Convert.ToHexString(CursorFingerprint.Current(role));
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Clock : IMonotonicClock { private readonly Stopwatch timer = Stopwatch.StartNew(); public TimeSpan Now => timer.Elapsed; }
}


