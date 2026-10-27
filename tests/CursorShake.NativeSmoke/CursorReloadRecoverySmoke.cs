using CursorShake.App;
using CursorShake.Core.Shake;
using CursorShake.Core.Input;
using CursorShake.Windows.Input;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CursorShake.Core.Runtime;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Native;
using CursorShake.Windows.Recovery;

namespace CursorShake.NativeSmoke;

// Native mutation QA. Root must authorize an exclusive run after reviewing this host.
internal static class CursorReloadRecoverySmoke
{
    internal static int Run(string root, string bundled, string guard, string output)
    {
        string? failure = null; bool restored = false; var cases = new List<object>();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        try { Task.Run(Execute).GetAwaiter().GetResult(); restored = true; }
        catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = failure is null && restored, Restored = restored,
            MutationAttempted = true, Failure = failure, Cases = cases }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output); if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null && restored ? 0 : 1;

        async Task Execute()
        {
            Need(!Directory.Exists(root), "A fresh QA root is required.");
            Need(!Process.GetProcessesByName("CursorShake").Any(), "A real CursorShake process is active; QA abstained.");
            Need(!Process.GetProcessesByName("CursorShake.RecoveryGuard").Any(), "A recovery helper is active; QA abstained.");
            Directory.CreateDirectory(root);
            string backup = Path.Combine(root, Guid.NewGuid().ToString("N")); RecoveryManifest original;
            using (var construction = new RecoveryConstruction(backup))
            { original = BaselineStore.Capture(backup, construction, null, out _, out _); construction.Complete(); }
            var baseline = original.Cursors.ToDictionary(c => c.Role, c => c.Fingerprint);
            // Reload before introducing Bibata: inherited registry defaults must also reproduce all17 originals.
            // If that check fails, compensate only the scheme written by this explicit test reload and abort.
            var preflightWritten = new Dictionary<CursorRole, string>();
            try
            {
                Reload(); foreach (var role in CursorRoles.All) preflightWritten[role] = Hash(role);
                Need(Equal(baseline), "Registered scheme reload does not reproduce all17 captured baseline roles.");
            }
            finally { if (preflightWritten.Count == CursorRoles.All.Count && !Equal(baseline)) Compensate(preflightWritten); }
            cases.Add(new { Case = "RegisteredReloadPreflight", All17Matched = true, Backup = backup });
            // Recovery verifies all17 roles directly; it does not require foreground input ownership.
            await Scenario("reload-budget");
            await Scenario("foreign-scheme");
            await Scenario("transparent-arrow");
            await Scenario("baseline-disappears-during-normalization");
            await Scenario("coordinator-latest");
            await Scenario("coordinator-dispose-pending");
            await Scenario("coordinator-hardstop-pending");
            await Scenario("configuration-reload");
            await Scenario("idle-audit");
            await Scenario("close-after-recovery");
            await Scenario("registry-change");
            await Scenario("normalization-barrier");
            await Scenario("normalization-timeout");
            await Scenario("normalization-failure");
            Need(Equal(baseline), "Final17-role restoration is incomplete.");
            cases.Add(new { Case = "FinalBaselineProof", Roles = CursorRoles.All.Select(role => new { Role = role.ToString(), Before = baseline[role], After = Hash(role) }).ToArray() });

            async Task Scenario(string scenario)
            {
                bool foreign = scenario is "foreign-scheme" or "transparent-arrow" or "baseline-disappears-during-normalization";
                string state = Path.Combine(root, scenario);
                var expected = new Dictionary<CursorRole, string>(); WindowsCursorEngine? engine = null;
                Process? helper = null; RecoveryManifest? sessionManifest = null; string? receipt = null;
                bool publicationAttempted = false, completedRecoveryScenario = false; string? closeFailure = null;
                RegistryKey? registryFixture = null; string? registryName = null, registryToken = null; bool registryWritten = false;
                var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<string>();
                ConfigurationCoordinator? coordinator = null; PointerRuntimeLoop? configurationRuntime = null; ShakeDetector? configurationDetector = null;
                Action<CursorEngineStatus>? availabilityHandler = null;
                try
                {
                    var scenarioClock = new Clock();
                    engine = new WindowsCursorEngine(state, guard, scenarioClock);
                    engine.Error += diagnostics.Enqueue;
                    engine.OwnershipLost += diagnostics.Enqueue;
                    receipt = Directory.EnumerateDirectories(Path.Combine(state, "recovery")).Single();
                    sessionManifest = BaselineStore.ReadManifest(receipt);
                    helper = Process.GetProcessesByName("CursorShake.RecoveryGuard").Single(p => string.Equals(p.MainModule?.FileName, Path.GetFullPath(guard), StringComparison.OrdinalIgnoreCase));
                    int helperId = helper.Id; long helperStart = helper.StartTime.ToUniversalTime().Ticks;
                    var prepared = new ThemePreparer(bundled).Prepare(new AppConfiguration { Theme = "bibata-ice" }, root);
                    publicationAttempted = true;
                    var applied = await engine.ApplyThemeAsync(prepared, 16).WaitAsync(TimeSpan.FromSeconds(20));
                    Need(applied.Applied, "Initial Bibata application failed: " + applied.Error);
                    await Quiesce(engine);
                    foreach (var role in CursorRoles.All) expected[role] = Hash(role);
                    if (foreign)
                    {
                        bool transientBaseline = scenario == "baseline-disappears-during-normalization";
                        var normalization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int normalizationCalls = 0;
                        if (transientBaseline) engine.NormalizeBeforeRecovery = () => { Interlocked.Increment(ref normalizationCalls); return normalization.Task; };
                        bool transparent = scenario == "transparent-arrow";
                        var pixels = transparent ? new CursorPixels(32, 32, 0, 0, new byte[32 * 32 * 4], Enumerable.Repeat((byte)0xFF, 4 * 32).ToArray()) : CrashGuardSmoke.Colored(41, 211);
                        using var replacement = CursorNative.Create(pixels);
                        Need(CursorNative.SetSystemCursor(replacement.Transfer(), (uint)CursorRole.Arrow), "Cannot install the test-owned foreign Arrow.");
                        expected[CursorRole.Arrow] = Hash(CursorRole.Arrow);
                        if (transparent)
                        {
                            var actual = CursorNative.Capture(CursorNative.LoadCursor(0, (nint)(uint)CursorRole.Arrow));
                            Need(actual.Width == 32 && actual.Height == 32 && actual.HotspotX == 0 && actual.HotspotY == 0 && actual.Bgra.All(value => value == 0) && actual.AndMask.All(value => value == 0xFF), "The transparent fixture does not match the CU pattern.");
                        }
                        long before = engine.PublishedFrames;
                        engine.CheckExternalChanges();
                        await Until(() => engine.Status == CursorEngineStatus.ExternalChange, "A foreign cursor did not pause the engine.");
                        long inspectionsBefore = engine.BaselineInspectionCount;
                        var held = Stopwatch.StartNew();
                        while (held.Elapsed < TimeSpan.FromMilliseconds(2200))
                        {
                            engine.CheckExternalChanges(); engine.SetScale(new(5, scenarioClock.Now, 201));
                            await Task.Delay(10);
                        }
                        long inspectionDelta = engine.BaselineInspectionCount - inspectionsBefore;
                        Need(inspectionDelta is >= 1 and <= 4, "Foreign-cursor inspection was absent or exceeded the bounded1Hz watch.");
                        engine.CheckExternalChanges(); engine.Normalize();
                        using (var candidate = Prepare())
                        {
                            var pendingApply = await engine.ApplyThemeAsync(candidate, 16).WaitAsync(TimeSpan.FromSeconds(10));
                            Need(!pendingApply.Applied && pendingApply.ErrorCode == "CursorRecoveryPending", "A valid configuration candidate was rejected as an error instead of remaining pending.");
                            Need(candidate.Roles[CursorRole.Arrow].Frames[0].Rasterize(32, 32).Width == 32, "The engine disposed a candidate that still belongs to its waiting caller.");
                            cases.Add(new { Case = "ConfigurationCandidateRemainsPendingDuringSoftWait", pendingApply.Applied, pendingApply.ErrorCode });
                        }
                        Need(engine.AutomaticRecoveries == 0 && engine.PublishedFrames == before && diagnostics.IsEmpty, "Soft waiting overwrote a foreign cursor or raised a hard-stop diagnostic.");
                        Need(Equal(expected), "Foreign Arrow or another paused role was overwritten.");
                        cases.Add(new { Case = transparent ? "TransparentCursorSoftWait" : "ForeignCursorSoftWait", Recoveries = engine.AutomaticRecoveries, Publications = engine.PublishedFrames, Status = engine.Status.ToString(), HeldMilliseconds = 2200, Diagnostics = diagnostics.ToArray(), InspectionDelta = inspectionDelta });
                        Reload(); expected = baseline.ToDictionary(x => x.Key, x => x.Value);
                        if (transientBaseline)
                        {
                            await Until(() => Volatile.Read(ref normalizationCalls) == 1, "A ready baseline did not begin normalization.");
                            using var interrupted = CursorNative.Create(CrashGuardSmoke.Colored(41, 219));
                            Need(CursorNative.SetSystemCursor(interrupted.Transfer(), (uint)CursorRole.Arrow), "Cannot interrupt the ready baseline with the owned foreign fixture.");
                            expected[CursorRole.Arrow] = Hash(CursorRole.Arrow); engine.CheckExternalChanges();
                            await Task.Delay(2200);
                            Need(engine.AutomaticRecoveries == 0 && engine.PublishedFrames == before && diagnostics.IsEmpty && Equal(expected), "A disappearing baseline incorrectly timed out, recovered, or overwrote the foreign cursor.");
                            normalization.SetResult(); Reload(); expected = baseline.ToDictionary(x => x.Key, x => x.Value);
                        }
                        await Until(() => engine.AutomaticRecoveries == 1, "The original baseline did not resume recovery after a soft wait.");
                        await Quiesce(engine);
                        Need(!helper.HasExited && helper.Id == helperId && BaselineStore.ReadManifest(receipt).Nonce == sessionManifest.Nonce, "Soft-wait recovery replaced the guard or session.");
                        Need(diagnostics.IsEmpty, "Baseline restoration after a soft wait raised a diagnostic.");
                        if (transientBaseline) Need(normalizationCalls == 1, "One incident normalized more than once after a disappearing baseline.");
                        foreach (var role in CursorRoles.All) expected[role] = Hash(role);
                        cases.Add(new { Case = transientBaseline ? "DisappearingBaselineReturnedToWatchAndRecovered" : transparent ? "TransparentCursorOriginalBaselineRecovered" : "ForeignCursorOriginalBaselineRecovered", Recoveries = engine.AutomaticRecoveries, Publications = engine.PublishedFrames, GuardPid = helperId, Session = sessionManifest.Nonce });
                    }
                    else if (scenario.StartsWith("coordinator-", StringComparison.Ordinal))
                    {
                        var workspace = new WorkspaceStore(Path.Combine(root, scenario + "-workspace"), Path.Combine(root, scenario + "-locator"), persistLocator: false);
                        workspace.EnsureConfiguration();
                        var initial = workspace.Read(); workspace.ProtectedUpdate(text => ConfigurationEdits.SetTheme(text, "bibata-ice"), initial.Revision);
                        var profile = new GestureProfile();
                        var input = new PointerInput(profile, () => PointerButtons.None, (_, _) => (1, 96));
                        configurationDetector = new ShakeDetector(input, engine, scenarioClock, profile);
                        configurationRuntime = new PointerRuntimeLoop(scenarioClock, input, configurationDetector);
                        var loop = configurationRuntime; var gesture = configurationDetector;
                        int unavailable = engine.Status == CursorEngineStatus.Available ? 0 : 1;
                        availabilityHandler = status =>
                        {
                            bool captured = status != CursorEngineStatus.Available;
                            Volatile.Write(ref unavailable, captured ? 1 : 0);
                            _ = AppHost.QueueEngineAvailability(loop, gesture, captured, () => Volatile.Read(ref unavailable) != 0);
                        };
                        engine.StatusChanged += availabilityHandler;
                        engine.NormalizeBeforeRecovery = () => AppHost.QueueEngineAvailability(loop, gesture, true, () => Volatile.Read(ref unavailable) != 0);
                        coordinator = new ConfigurationCoordinator(workspace, new ThemePreparer(bundled), engine, input, gesture, loop, scenarioClock, () => Volatile.Read(ref unavailable) != 0);
                        var configurationErrors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
                        coordinator.Error += configurationErrors.Enqueue;
                        await coordinator.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20));
                        Need(coordinator.Effective?.Theme == "bibata-ice" && configurationErrors.IsEmpty, "Coordinator did not establish its initial effective Bibata configuration.");
                        await Quiesce(engine); foreach (var role in CursorRoles.All) expected[role] = Hash(role);
                        string bibataArrow = expected[CursorRole.Arrow];
                        using (var replacement = CursorNative.Create(CrashGuardSmoke.Colored(41, 207)))
                            Need(CursorNative.SetSystemCursor(replacement.Transfer(), (uint)CursorRole.Arrow), "Cannot install the coordinator's owned foreign fixture.");
                        expected[CursorRole.Arrow] = Hash(CursorRole.Arrow);
                        long before = engine.PublishedFrames; engine.CheckExternalChanges();
                        await Until(() => engine.IsWaitingForOriginalScheme, "Coordinator fixture did not enter soft waiting.");
                        await Task.Delay(1200);
                        if (scenario == "coordinator-latest")
                        {
                            Save("windows"); await Until(() => coordinator.PendingTheme == "windows", "Saved Windows theme did not become pending.");
                        }
                        Save("nordzy-white");
                        await Until(() => coordinator.PendingTheme == "nordzy-white", "Saved Nordzy theme did not become pending through the file watcher.");
                        Need(coordinator.Effective?.Theme == "bibata-ice" && configurationErrors.IsEmpty && diagnostics.IsEmpty && engine.PublishedFrames == before && Equal(expected), "Pending configuration changed the effective theme, wrote cursors or raised an error.");
                        if (scenario == "coordinator-dispose-pending")
                        {
                            var elapsed = Stopwatch.StartNew();
                            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                            Need(configurationErrors.IsEmpty && engine.PublishedFrames == before && Equal(expected), "Cancelling a pending coordinator published or failed.");
                            cases.Add(new { Case = scenario, DisposeMilliseconds = elapsed.Elapsed.TotalMilliseconds, NoInputRequired = true, Publications = engine.PublishedFrames });
                        }
                        else if (scenario == "coordinator-hardstop-pending")
                        {
                            registryFixture = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", writable: true) ?? throw new IOException("The cursor settings key is absent.");
                            registryName = "CursorShake.QA." + Guid.NewGuid().ToString("N"); registryToken = Guid.NewGuid().ToString("N");
                            Need(!registryFixture.GetValueNames().Contains(registryName, StringComparer.OrdinalIgnoreCase), "The owned registry identity exists already.");
                            registryFixture.SetValue(registryName, registryToken, RegistryValueKind.String); registryFixture.Flush(); registryWritten = true;
                            // Status was ExternalChange already: only the recovery-wait change can wake the pending descriptor.
                            await Until(() => !engine.IsWaitingForOriginalScheme && !configurationErrors.IsEmpty && coordinator.PendingTheme is null, "A hard stop did not wake/report the pending coordinator.");
                            Need(coordinator.Effective?.Theme == "bibata-ice" && engine.PublishedFrames == before && Equal(expected), "The hard-stopped pending descriptor was applied.");
                            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                            cases.Add(new { Case = scenario, KeptEffectiveTheme = coordinator.Effective?.Theme, Errors = configurationErrors.Select(error => error.Message).ToArray(), NoInputRequired = true });
                        }
                        else
                        {
                            Reload(); expected = baseline.ToDictionary(x => x.Key, x => x.Value);
                            await Until(() => coordinator.Effective?.Theme == "nordzy-white" && coordinator.PendingTheme is null && !coordinator.IsApplying, "The latest saved Nordzy configuration did not become effective after baseline recovery.");
                            await Quiesce(engine);
                            Need(configurationErrors.IsEmpty && diagnostics.IsEmpty && Hash(CursorRole.Arrow) != bibataArrow, "Latest configuration recovery did not produce the new cursor theme cleanly.");
                            foreach (var role in CursorRoles.All) expected[role] = Hash(role);
                            Need(!helper.HasExited && BaselineStore.ReadManifest(receipt).Nonce == sessionManifest.Nonce, "Coordinator recovery replaced its session or guard.");
                            cases.Add(new { Case = scenario, EffectiveTheme = coordinator.Effective?.Theme, Recoveries = engine.AutomaticRecoveries, Publications = engine.PublishedFrames, NoInputRequired = true });
                        }
                        void Save(string theme)
                        {
                            var current = workspace.Read(); workspace.ProtectedUpdate(text => ConfigurationEdits.SetTheme(text, theme), current.Revision);
                            // No RequestReload/button call: the real FileMonitor must deliver the saved revision.
                        }
                    }
                    else if (scenario is "normalization-barrier" or "normalization-timeout" or "normalization-failure")
                    {
                        var normalization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        int normalizationCalls = 0;
                        engine.NormalizeBeforeRecovery = () => { Interlocked.Increment(ref normalizationCalls); return normalization.Task; };
                        long before = engine.PublishedFrames;
                        Reload(); expected = baseline.ToDictionary(x => x.Key, x => x.Value);
                        engine.CheckExternalChanges();
                        await Until(() => Volatile.Read(ref normalizationCalls) == 1, "Recovery did not request domain normalization.");
                        engine.SetScale(new(5, scenarioClock.Now, 191)); // A pre-recovery gesture target must never escape the barrier.
                        if (scenario == "normalization-failure") normalization.SetException(new IOException("Deliberate fixture normalization failure"));
                        if (scenario == "normalization-barrier")
                        {
                            await Task.Delay(200); // Deliberately hold the admitted recovery well below its1second limit.
                            Need(engine.PublishedFrames == before && engine.AutomaticRecoveries == 0 && Equal(baseline), "Recovery published before domain normalization completed.");
                            normalization.SetResult();
                            await Until(() => engine.AutomaticRecoveries == 1, "Recovery did not resume after successful normalization.");
                            await Quiesce(engine);
                            foreach (var role in CursorRoles.All) expected[role] = Hash(role);
                            Need(diagnostics.IsEmpty, "Successful normalization/recovery raised an error.");
                        }
                        else
                        {
                            await Until(() => !diagnostics.IsEmpty, "A failed or overdue normalization did not cede ownership.");
                            Need(engine.Status == CursorEngineStatus.ExternalChange, "Normalization failure was not a ceded recovery.");
                            Need(engine.AutomaticRecoveries == 0 && engine.PublishedFrames == before && Equal(baseline), "Failed normalization allowed native publication.");
                            if (scenario == "normalization-timeout") normalization.SetResult();
                            engine.CheckExternalChanges(); engine.SetScale(new(3, scenarioClock.Now, 192));
                            await Task.Delay(200); // A late task completion must not reopen an already abandoned recovery.
                            Need(engine.AutomaticRecoveries == 0 && engine.PublishedFrames == before && Equal(baseline), "A late/stale target revived abandoned recovery.");
                        }
                        Need(Volatile.Read(ref normalizationCalls) == 1, "One reload incident requested normalization more than once.");
                        cases.Add(new { Case = scenario, NormalizationCalls = normalizationCalls, Recoveries = engine.AutomaticRecoveries, PublishedBefore = before, PublishedAfter = engine.PublishedFrames, Status = engine.Status.ToString(), Diagnostics = diagnostics.ToArray() });
                    }
                    else if (scenario == "registry-change")
                    {
                        registryFixture = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", writable: true) ?? throw new IOException("The registered cursor key is absent; no key was created.");
                        registryName = "CursorShake.QA." + Guid.NewGuid().ToString("N"); registryToken = Guid.NewGuid().ToString("N");
                        Need(!registryFixture.GetValueNames().Contains(registryName, StringComparer.OrdinalIgnoreCase), "QA registry identity already exists.");
                        registryFixture.SetValue(registryName, registryToken, RegistryValueKind.String); registryFixture.Flush(); registryWritten = true;
                        long before = engine.PublishedFrames; Reload(); expected = baseline.ToDictionary(x => x.Key, x => x.Value);
                        engine.CheckExternalChanges();
                        await Until(() => engine.Status == CursorEngineStatus.ExternalChange && !diagnostics.IsEmpty, "A changed registry snapshot did not cede ownership.");
                        Need(engine.AutomaticRecoveries == 0 && engine.PublishedFrames == before, "A changed registry snapshot was automatically adopted.");
                        Need(Equal(baseline), "Registry-conflict recovery changed the registered baseline.");
                        cases.Add(new { Case = "ChangedRegistryRejected", Recoveries = engine.AutomaticRecoveries, Publications = engine.PublishedFrames, OwnRegistryValue = registryName });
                        Need(registryFixture.GetValueKind(registryName) == RegistryValueKind.String && registryFixture.GetValue(registryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string == registryToken, "The owned registry fixture changed before removal.");
                        registryFixture.DeleteValue(registryName, throwOnMissingValue: true); registryFixture.Flush(); registryWritten = false;
                        Reload(); engine.CheckExternalChanges(); await Task.Delay(1200);
                        Need(engine.AutomaticRecoveries == 0 && engine.PublishedFrames == before && engine.Status == CursorEngineStatus.ExternalChange && Equal(baseline), "Restoring the registry revived a hard-stopped recovery.");
                        cases.Add(new { Case = "RegistryRejectionRemainsHardStoppedAfterOwnValueRemoval", OwnRegistryValueRemoved = registryName, Recoveries = engine.AutomaticRecoveries });
                    }
                    else if (scenario is "configuration-reload" or "idle-audit" or "close-after-recovery")
                    {
                        long before = engine.PublishedFrames;
                        Reload(); expected = baseline.ToDictionary(x => x.Key, x => x.Value);
                        if (scenario == "configuration-reload")
                        {
                            // No explicit CheckExternalChanges: saving configuration is the first observer of the reload.
                            var candidate = Prepare();
                            var result = await engine.ApplyThemeAsync(candidate, 16).WaitAsync(TimeSpan.FromSeconds(15));
                            Need(result.Applied, "A valid theme update concurrent with reload failed: " + result.Error);
                        }
                        else
                        {
                            if (scenario == "close-after-recovery") engine.CheckExternalChanges();
                            // Idle-audit has no observation, gesture, scale request or explicit check to wake recovery.
                            await Until(() => engine.AutomaticRecoveries == 1, "Idle audit did not discover the registered-scheme reload.");
                        }
                        await Until(() => engine.AutomaticRecoveries == 1, "Reload recovery did not complete for " + scenario);
                        await Quiesce(engine);
                        Need(engine.PublishedFrames >= before + CursorRoles.All.Count, "Recovery did not republish17 roles.");
                        Need(diagnostics.IsEmpty, "A valid reload/update produced an error or ownership-loss diagnostic.");
                        foreach (var role in CursorRoles.All) expected[role] = Hash(role);
                        Need(!helper.HasExited && helper.Id == helperId && BaselineStore.ReadManifest(receipt).Nonce == sessionManifest.Nonce, "Configuration/audit recovery replaced its guard/session.");
                        cases.Add(new { Case = scenario, Recoveries = engine.AutomaticRecoveries, Publications = engine.PublishedFrames, GuardPid = helperId, Session = sessionManifest.Nonce, Diagnostics = diagnostics.ToArray() });
                    }
                    else
                    {
                        for (int attempt = 1; attempt <= 4; attempt++)
                        {
                            long before = engine.PublishedFrames;
                            Reload(); expected = baseline.ToDictionary(x => x.Key, x => x.Value);
                            engine.CheckExternalChanges();
                            if (attempt <= 3)
                            {
                                await Until(() => engine.AutomaticRecoveries == attempt, "Permitted automatic recovery did not finish.");
                                await Quiesce(engine);
                                Need(engine.PublishedFrames >= before + CursorRoles.All.Count, "Recovery did not republish all17 roles.");
                                Need(!helper.HasExited && helper.Id == helperId && helper.StartTime.ToUniversalTime().Ticks == helperStart, "Recovery replaced or lost the original guard.");
                                Need(Directory.Exists(receipt) && BaselineStore.ReadManifest(receipt).Nonce == sessionManifest.Nonce, "Recovery replaced its session/baseline.");
                                foreach (var role in CursorRoles.All) expected[role] = Hash(role);
                                Need(engine.Status != CursorEngineStatus.ExternalChange && engine.Status != CursorEngineStatus.Degraded, "Admitted recovery remained unavailable.");
                            }
                            else
                            {
                                await Until(() => engine.Status == CursorEngineStatus.ExternalChange, "Fourth recovery was not rejected.");
                                Need(engine.AutomaticRecoveries == 3 && engine.PublishedFrames == before, "Fourth reload exceeded the3/60s limit.");
                                Need(Equal(baseline), "Budget exhaustion overwrote the registered baseline.");
                            }
                            cases.Add(new { Case = "RollingRecoveryBudget", Attempt = attempt, Recoveries = engine.AutomaticRecoveries,
                                PublishedBefore = before, PublishedAfter = engine.PublishedFrames, GuardPid = helperId, Session = sessionManifest.Nonce, Status = engine.Status.ToString() });
                        }
                    }
                    completedRecoveryScenario = engine.AutomaticRecoveries > 0;
                }
                finally
                {
                    if (coordinator is not null) await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    if (engine is not null)
                    {
                        var stopped = await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
                        if (completedRecoveryScenario && scenario is ("configuration-reload" or "idle-audit" or "close-after-recovery" or "normalization-barrier" or "foreign-scheme" or "transparent-arrow" or "baseline-disappears-during-normalization" or "coordinator-latest"))
                        {
                            bool restoredByProduct = stopped.AllOwnedRestored && Equal(baseline);
                            if (!restoredByProduct) closeFailure = "Normal close after recovery required test compensation or failed to restore17 originals.";
                            cases.Add(new { Case = "ProductCloseAfterRecovery", Scenario = scenario, stopped.AllOwnedRestored, All17BaselineBeforeTestCompensation = restoredByProduct });
                        }
                    }
                    if (engine is not null && availabilityHandler is not null) engine.StatusChanged -= availabilityHandler;
                    if (engine is not null) engine.NormalizeBeforeRecovery = null;
                    configurationRuntime?.Dispose(); configurationDetector?.Dispose();
                    try
                    {
                        if (registryWritten)
                        {
                            Need(registryFixture is not null && registryName is not null && registryFixture.GetValueKind(registryName) == RegistryValueKind.String && registryFixture.GetValue(registryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string == registryToken, "The test registry identity changed; its value was preserved.");
                            registryFixture!.DeleteValue(registryName!, throwOnMissingValue: true); registryFixture.Flush(); registryWritten = false;
                            Need(!registryFixture.GetValueNames().Contains(registryName!, StringComparer.OrdinalIgnoreCase), "The owned registry fixture remains.");
                            cases.Add(new { Case = "OwnRegistryFixtureRetired", OwnRegistryValue = registryName });
                        }
                    }
                    finally { registryFixture?.Dispose(); }
                    if (publicationAttempted && !Equal(baseline))
                    {
                        Need(expected.Count == CursorRoles.All.Count, "Cannot prove ownership of the partially published fixture; preserve backup and receipt.");
                        Compensate(expected);
                    }
                    Need(Equal(baseline), "Cannot retire a QA recovery receipt until all17 baselines match.");
                    if (receipt is not null && sessionManifest is not null && Directory.Exists(receipt))
                    {
                        // Product correctly cedes an external scheme; after verified TEST compensation only,
                        // retire this fixture's own guard ledger so it never waits for the entire host to exit.
                        Need(BaselineStore.ExistsWithIdentity(receipt, sessionManifest), "QA recovery receipt identity changed.");
                        using (var lease = new RecoveryLease(sessionManifest))
                        using (var ledger = new OwnershipLedger(sessionManifest.LedgerName, sessionManifest.Cursors.Count, false)) ledger.Disarm();
                        // Disarm BEFORE Dispose sends Stop: otherwise an armed guard waits for parent exit.
                        // Engine Stop has drained publication already; all17 exact original hashes are proven above.
                    }
                    engine?.Dispose();
                    if (helper is not null)
                    {
                        try { Need(helper.WaitForExit(15000), "The fixture's guard did not finish after verified compensation."); }
                        finally { helper.Dispose(); }
                    }
                    Need(RecoveryBootstrap.Inspect(state).Count == 0, "A QA recovery receipt remains; preserve it for investigation.");
                    cases.Add(new { Case = "FixtureCleanup", Foreign = foreign, All17Restored = true, GuardExited = true, NoReceipts = true });
                    Need(closeFailure is null, closeFailure ?? "");
                }
                PreparedTheme Prepare() => new ThemePreparer(bundled).Prepare(new AppConfiguration { Theme = "bibata-ice" }, root);
            }
            bool Equal(IReadOnlyDictionary<CursorRole, string> expected) => CursorRoles.All.All(role => Hash(role) == expected[role]);
            void Compensate(IReadOnlyDictionary<CursorRole, string> expected)
            {
                Need(CursorRoles.All.All(role => Hash(role) == expected[role] || Hash(role) == baseline[role]), "Desktop changed outside the QA fixture; no forced compensation was attempted.");
                foreach (var entry in original.Cursors)
                {
                    if (Hash(entry.Role) == baseline[entry.Role]) continue;
                    Need(Hash(entry.Role) == expected[entry.Role], "Role changed before conditional QA cleanup: " + entry.Role);
                    var pixels = BaselineStore.ReadPixels(Path.Combine(backup, entry.PixelFile));
                    using var source = LoadOriginal(entry, pixels);
                    Need(CursorNative.SetSystemCursor(source.Transfer(), (uint)entry.Role), "Conditional QA restoration failed: " + entry.Role);
                    Need(Hash(entry.Role) == entry.Fingerprint, "Restored role differs: " + entry.Role);
                }
            }
            SafeCursorHandle LoadOriginal(BaselineCursor entry, CursorPixels pixels)
            {
                if (entry.SourceFile is null) return CursorNative.Create(pixels);
                string path = Path.Combine(backup, entry.SourceFile);
                using var source = ProtectedFile.Open(path); Need(source.Hash() == entry.SourceHash, "Original ANI/CUR bytes changed.");
                var loaded = CursorNative.LoadFile(path, pixels.Width, pixels.Height);
                if (CursorFingerprint.Of(CursorNative.Capture(loaded.DangerousGetHandle())) == entry.Fingerprint) return loaded;
                loaded.Dispose(); throw new InvalidDataException("Original ANI/CUR fingerprint differs.");
            }
        }
    }
    private static async Task Quiesce(WindowsCursorEngine engine)
    {
        engine.Observe(new(CursorRole.Arrow, false, (nint)111, 32));
        using var barrierTheme = new PreparedTheme("barrier", new Dictionary<CursorRole, CursorShake.Windows.Cursors.Formats.CursorAsset>());
        await engine.ApplyThemeAsync(barrierTheme, double.NaN).WaitAsync(TimeSpan.FromSeconds(10));
        var previous = CursorRoles.All.ToDictionary(r => r, Hash);
        int stableSamples = 0;
        var settling = Stopwatch.StartNew();
        while (stableSamples < 3)
        {
            Need(settling.Elapsed < TimeSpan.FromSeconds(2), "Hidden/static fixture fingerprints did not settle.");
            await Task.Delay(100);
            var current = CursorRoles.All.ToDictionary(r => r, Hash);
            stableSamples = CursorRoles.All.All(role => current[role] == previous[role]) ? stableSamples + 1 : 0;
            previous = current;
        }
    }
    private static async Task Until(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException(failure); await Task.Delay(10); }
    }
    private static string Hash(CursorRole role) => Convert.ToHexString(CursorFingerprint.Current(role));
    private static void Reload() => Need(SystemParametersInfo(0x0057, 0, 0, 0), "SPI_SETCURSORS failed.");
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Clock : IMonotonicClock { private readonly Stopwatch timer = Stopwatch.StartNew(); public TimeSpan Now => timer.Elapsed; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, nint value, uint flags);
}
