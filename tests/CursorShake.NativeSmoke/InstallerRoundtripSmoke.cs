using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Lifecycle;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace CursorShake.NativeSmoke;

// Real per-user installer QA. Root must review this harness and grant exclusive execution first.
// Never launches AppHost, input, a cursor engine, or a scheduled task. Never kills any process.
internal static class InstallerRoundtripSmoke
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\R3Neer.CursorShake_is1";
    private const string TaskSource = "CursorShake";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static int Run(string candidateExe, string reportPath, bool allUsers = false)
    {
        var audit = new Audit { Candidate = Path.GetFullPath(candidateExe), Report = Path.GetFullPath(reportPath), AllUsers = allUsers };
        Scheduler? scheduler = null;
        TaskBackup? originalTask = null;
        DataBackup? originalData = null;
        List<Process> originals = [];
        bool snapshotReady = false, mutated = false;
        FileStream? candidatePin = null;
        DirectoryProtection? machineParentPin = null;
        try
        {
            Require(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000), "Windows11 or later is required.");
            using var identity = WindowsIdentity.GetCurrent();
            Require(Environment.UserInteractive && Process.GetCurrentProcess().SessionId != 0, "Interactive user context is required.");
            Require(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "QA must run without elevation.");
            audit.Sid = identity.User!.Value;
            string project = FindProject();
            string qaParent = Path.Combine(project, "artifacts", "installer-qa");
            using (DirectoryProtection.Acquire(qaParent, create: true)) { }
            audit.Root = Path.Combine(qaParent, Guid.NewGuid().ToString("N"));
            audit.App = allUsers ? Path.Combine(@"D:\Program Files", "CursorShake.QA." + Path.GetFileName(audit.Root)) : Path.Combine(audit.Root, "app");
            if (allUsers)
            {
                machineParentPin = DirectoryProtection.Acquire(@"D:\Program Files");
                var owner = new DirectoryInfo(@"D:\Program Files").GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
                var trustedInstaller = new NTAccount("NT SERVICE", "TrustedInstaller").Translate(typeof(SecurityIdentifier));
                Require(Equals(owner, trustedInstaller), "Machine QA destination parent must retain its trusted protected owner.");
            }
            audit.Temp = Path.Combine(audit.Root, "temp");
            Directory.CreateDirectory(audit.Root); Directory.CreateDirectory(audit.Temp);
            using var rootPin = DirectoryProtection.Acquire(audit.Root);
            Require(audit.Candidate.StartsWith(Path.Combine(project, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(audit.Candidate).EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase), "Candidate must be a Setup artifact of this project.");
            candidatePin = new FileStream(audit.Candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
            audit.CandidateHash = Convert.ToHexString(SHA256.HashData(candidatePin));
            candidatePin.Position = 0;
            Require(File.ReadAllText(audit.Candidate + ".sha256").Trim().Equals(audit.CandidateHash.ToLowerInvariant() + "  " + Path.GetFileName(audit.Candidate), StringComparison.OrdinalIgnoreCase), "Candidate sidecar hash differs.");
            using (var build = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(audit.Candidate)!, "build.json"))))
                Require(build.RootElement.GetProperty("SetupSHA256").GetString()!.Equals(audit.CandidateHash, StringComparison.OrdinalIgnoreCase) && !build.RootElement.GetProperty("PreviewOnly").GetBoolean() && build.RootElement.GetProperty("LifecycleIntegrated").GetBoolean(), "Candidate does not match lifecycle build metadata.");
            string expectedManifest = Path.Combine(project, "artifacts", "installer-payload", "manifest.sha256");
            using (var staged = ProtectedFile.Open(expectedManifest)) audit.ExpectedManifestHash = staged.Hash();
            audit.ExpectedPayloadFiles = File.ReadAllLines(expectedManifest).Length;
            string compilerManifest = Path.Combine(Path.GetDirectoryName(audit.Candidate)!, "payload-manifest.txt");
            var compiledLines = File.ReadAllLines(compilerManifest);
            Require(compiledLines.Any(line => line.Contains("manifest.sha256", StringComparison.OrdinalIgnoreCase) && line.Contains(audit.ExpectedManifestHash, StringComparison.OrdinalIgnoreCase)), "Candidate compiler manifest does not anchor the current staged manifest.");
            Require(ReadRegistrations().Count == 0, "CursorShake already has an uninstall registration; QA refuses to overwrite it.");
            string shortcut = Path.Combine(Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs), "CursorShake.lnk");
            Require(!File.Exists(shortcut), "An existing CursorShake Start Menu shortcut must not be overwritten by QA.");
            scheduler = new Scheduler(audit.Sid);
            originalTask = scheduler.Read();
            if (originalTask is not null) scheduler.AssertRestorable(originalTask);
            originalData = ReadData();
            originals = SnapshotOriginalProcesses(audit);
            snapshotReady = true;
            Write(Path.Combine(audit.Root, "before-mutation-backup.json"), new { audit.Candidate, audit.CandidateHash, audit.Sid, Task = originalTask, Data = originalData, OriginalProcesses = audit.OriginalProcesses });
            Require(!Directory.Exists(audit.App), "QA application destination is not fresh.");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(audit.App)).ToUpperInvariant()))).ToLowerInvariant();
            audit.Index = Path.Combine(Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonApplicationData : Environment.SpecialFolder.LocalApplicationData), "CursorShake.Installations", hash);
            Require(!Directory.Exists(audit.Index), "QA recovery index already exists.");
            mutated = true;
            ExecutePhase(audit, "install", audit.Candidate, SetupArguments(audit, "install", ""));
            VerifyInstalled(audit);
            Require(scheduler.Read() is null, "Deselected startup should leave no current-user startup task.");
            VerifyDataUnchanged(originalData);
            ExecutePhase(audit, "upgrade", audit.Candidate, SetupArguments(audit, "upgrade", "autostart"));
            VerifyInstalled(audit);
            var qaTask = scheduler.Read() ?? throw new IOException("Autostart upgrade did not create a task.");
            scheduler.AssertQaTask(qaTask, audit.App);
            scheduler.ObserveExpectedQaTask(qaTask, audit.App);
            Require(qaTask.Enabled, "QA autostart task is disabled.");
            VerifyDataUnchanged(originalData);
            Uninstall(audit);
            Require(scheduler.Read() is null, "Uninstall did not remove its QA startup task.");
            audit.RoundtripCompleted = true;
        }
        catch (Exception error) { audit.Errors.Add(error.ToString()); }
        finally
        {
            // Preserve incomplete installations/evidence. Cleanup is attempted only via the verified own uninstaller.
            bool cleanupSafe = false;
            try { cleanupSafe = !LiveOwnedProcesses(audit).Any(); }
            catch (Exception liveness) { audit.Errors.Add(liveness.ToString()); }
            if (mutated && !audit.Uninstalled && cleanupSafe)
            {
                try { if (FindOwnUninstaller(audit, required: false) is not null) Uninstall(audit); }
                catch (Exception cleanup) { audit.Errors.Add("QA uninstall cleanup: " + cleanup); }
            }
            bool noLiveOwnedProcesses = false;
            try
            {
                audit.RemainingQaProcesses = LiveOwnedProcesses(audit).ToArray();
                noLiveOwnedProcesses = audit.RemainingQaProcesses.Length == 0;
                Require(noLiveOwnedProcesses, "Own installer/helper processes remain alive: startup restoration is deferred until controlled cancellation; original backup is preserved.");
            }
            catch (Exception liveness) { audit.Errors.Add(liveness.ToString()); }
            if (snapshotReady && scheduler is not null)
            {
                try { Require(noLiveOwnedProcesses, "Startup restoration refused while an installer/helper may still change the task."); scheduler.Restore(originalTask, audit.App); audit.OriginalTaskRestored = true; }
                catch (Exception restore) { audit.Errors.Add("Original startup task was NOT restored; use the preserved backup: " + restore); }
                try { VerifyDataUnchanged(originalData!); audit.DataUnchanged = true; }
                catch (Exception dataError) { audit.Errors.Add(dataError.ToString()); }
            }
            foreach (var original in originals)
            {
                try { Require(!original.HasExited, $"Original process {original.Id} exited during QA."); }
                catch (Exception changed) { audit.Errors.Add(changed.ToString()); }
                finally { original.Dispose(); }
            }
            scheduler?.Dispose();
            try { if (audit.CandidateHash is not null) Require(Hash(audit.Candidate) == audit.CandidateHash, "Candidate changed during QA."); }
            catch (Exception changed) { audit.Errors.Add(changed.ToString()); }
            finally { candidatePin?.Dispose(); }
            if (mutated)
            {
                try { audit.RemainingQaProcesses = LiveOwnedProcesses(audit).ToArray(); Require(audit.RemainingQaProcesses.Length == 0, "Own installer/helper processes remain; preserve QA state and inspect them."); }
                catch (Exception changed) { audit.Errors.Add(changed.ToString()); }
            }
            foreach (var tracked in audit.Tracked.Values) tracked.Handle.Dispose();
            machineParentPin?.Dispose();
            audit.Passed = audit.RoundtripCompleted && audit.Uninstalled && audit.OriginalTaskRestored && audit.DataUnchanged && audit.Errors.Count == 0;
            Directory.CreateDirectory(Path.GetDirectoryName(audit.Report)!);
            Write(audit.Report, audit);
        }
        Console.WriteLine(audit.Report);
        foreach (string error in audit.Errors) Console.Error.WriteLine(error);
        return audit.Passed ? 0 : 1;
    }

    private static string[] SetupArguments(Audit audit, string phase, string tasks) => [audit.AllUsers ? "/ALLUSERS" : "/CURRENTUSER", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/TASKS=" + tasks, "/DIR=" + audit.App, "/LOG=" + Path.Combine(audit.Root, phase + ".log")];
    private static void Uninstall(Audit audit)
    {
        string executable = FindOwnUninstaller(audit, required: true)!;
        ExecutePhase(audit, "uninstall-keep-data", executable, ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=" + Path.Combine(audit.Root, "uninstall.log")]);
        var retirement = Stopwatch.StartNew();
        while(retirement.Elapsed < TimeSpan.FromSeconds(15))
        {
            if(ConfirmedAbsent(audit.App) && ConfirmedAbsent(audit.Index) && ReadRegistrations().Count == 0)break;
            Thread.Sleep(100);
        }
        Require(ReadRegistrations().Count == 0, "Uninstall registration remains.");
        Require(ConfirmedAbsent(audit.App), "QA application folder remains; no forced recursive cleanup is performed.");
        Require(ConfirmedAbsent(audit.Index), "QA recovery index remains.");
        try { using var gate = Mutex.OpenExisting(InstallationGate.NameFor(audit.App)); throw new IOException("QA installation gate remains alive."); }
        catch (WaitHandleCannotBeOpenedException) { }
        audit.Uninstalled = true;
    }
    private static string? FindOwnUninstaller(Audit audit, bool required)
    {
        var registrations = ReadRegistrations();
        if (registrations.Count == 0) { if (required) throw new IOException("Own uninstall registration is absent."); return null; }
        foreach (var item in registrations) Require(item.Hive == (audit.AllUsers ? "LocalMachine" : "CurrentUser") && Same(item.Location, audit.App), "Uninstall registration does not belong to the QA installation.");
        var files = Directory.Exists(audit.App) ? Directory.GetFiles(audit.App, "unins*.exe") : [];
        Require(files.Length == 1, "Expected exactly one own uninstaller.");
        Require(registrations.All(item => OwnUninstallCommand(item.UninstallCommand, files[0])), "Uninstall command differs from the QA executable.");
        return files[0];
    }
    private static bool ConfirmedAbsent(string path)
    {
        try{using var protection=DirectoryProtection.Acquire(path);return false;}
        catch(FileNotFoundException){return true;}
    }
    private static bool OwnUninstallCommand(string command, string executable)
    {
        string value = command.Trim();
        if (value.Equals(executable, StringComparison.OrdinalIgnoreCase)) return true;
        string quoted = '"' + executable + '"';
        if (value.Equals(quoted, StringComparison.OrdinalIgnoreCase)) return true;
        return value.Equals(quoted + " /SILENT", StringComparison.OrdinalIgnoreCase) || value.Equals(quoted + " /VERYSILENT", StringComparison.OrdinalIgnoreCase);
    }
    private static void VerifyInstalled(Audit audit)
    {
        Require(File.ReadAllText(Path.Combine(audit.App, "maintenance-scope.txt")).Trim() == (audit.AllUsers ? "all-users" : "user"), "Installation scope differs from the requested QA mode.");
        var registrations = ReadRegistrations();
        Require(registrations.Count > 0 && registrations.All(item => item.Hive == (audit.AllUsers ? "LocalMachine" : "CurrentUser") && Same(item.Location, audit.App)), "Per-user installation registration differs.");
        string manifest = Path.Combine(audit.App, "manifest.sha256"); int count = 0;
        Require(Hash(manifest).Equals(audit.ExpectedManifestHash, StringComparison.OrdinalIgnoreCase), "Installed manifest differs from the candidate staged/compiled snapshot.");
        foreach (string line in File.ReadAllLines(manifest))
        {
            int split = line.IndexOf("  ", StringComparison.Ordinal);
            Require(split == 64, "Installed manifest record is malformed.");
            string relative = line[(split + 2)..]; string file = Path.GetFullPath(relative, audit.App);
            Require(Inside(file, audit.App) && !relative.Contains("..") && !relative.Contains(':') && Path.GetFileName(file) != ".cursorshake-build-owned", "Unsafe installed manifest path.");
            using var protectedFile = ProtectedFile.Open(file);
            Require(protectedFile.Hash().Equals(line[..64], StringComparison.OrdinalIgnoreCase), "Installed file hash differs: " + relative); count++;
        }
        Require(count == audit.ExpectedPayloadFiles, "Installed payload count differs from the candidate snapshot.");
        string users = Path.Combine(audit.Index, "Users");
        using (DirectoryProtection.Acquire(users)) { }
        foreach (string path in new[] { Path.GetDirectoryName(audit.Index)!, audit.Index, users }) VerifyIndexAcl(path, audit.Sid, audit.AllUsers, Same(path, users));
        Require(!Directory.EnumerateFileSystemEntries(users).Any(), "No AppHost was launched; the QA index should contain no user admission records.");
        audit.InstalledPayloadCounts.Add(count);
    }
    private static void VerifyIndexAcl(string path, string sid, bool allUsers = false, bool usersLeaf = false)
    {
        if (allUsers)
        {
            var acl = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            Require(acl.AreAccessRulesProtected && acl.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier machineOwner && machineOwner.Value is "S-1-5-18" or "S-1-5-32-544", "Machine recovery-index ownership differs: " + path);
            var expectedRules = new Dictionary<string, FileSystemRights>
            {
                ["S-1-5-18"] = FileSystemRights.FullControl,
                ["S-1-5-32-544"] = FileSystemRights.FullControl,
                ["S-1-5-32-545"] = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize | (usersLeaf ? FileSystemRights.CreateFiles : 0)
            };
            var actualRules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            Require(actualRules.Length == 3 && actualRules.All(rule => expectedRules.Remove(rule.IdentityReference.Value, out var rights) && rule.FileSystemRights == rights
                && rule.AccessControlType == AccessControlType.Allow && !rule.IsInherited && rule.PropagationFlags == PropagationFlags.None
                && rule.InheritanceFlags == (rule.IdentityReference.Value == "S-1-5-32-545" ? InheritanceFlags.None : InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)), "Machine recovery-index ACL differs: " + path);
            return;
        }
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        Require(security.AreAccessRulesProtected && security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && owner.Value == sid, "Recovery-index ownership/inheritance differs: " + path);
        var expected = new[] { sid, "S-1-5-18", "S-1-5-32-544" }.ToHashSet(StringComparer.Ordinal);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Require(rules.Length == expected.Count && rules.All(rule => expected.Remove(rule.IdentityReference.Value) && rule.FileSystemRights == FileSystemRights.FullControl && rule.AccessControlType == AccessControlType.Allow && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit) && rule.PropagationFlags == PropagationFlags.None && !rule.IsInherited), "Recovery-index ACL differs: " + path);
    }
    private static List<UninstallProof> ReadRegistrations()
    {
        var result = new List<UninstallProof>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var registry = RegistryKey.OpenBaseKey(hive, view); using var key = registry.OpenSubKey(UninstallKey);
            if (key is not null) result.Add(new(hive.ToString(), view.ToString(), key.GetValue("InstallLocation") as string ?? "", key.GetValue("UninstallString") as string ?? ""));
        }
        return result;
    }
    private static DataBackup ReadData()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\CursorShake");
        bool exists = key?.GetValueNames().Contains("Workspace", StringComparer.OrdinalIgnoreCase) == true;
        object? value = exists ? key!.GetValue("Workspace", null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        string? kind = exists ? key!.GetValueKind("Workspace").ToString() : null;
        string raw = JsonSerializer.Serialize(value); string? config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorShake", "config.toml"), hash = null;
        if (value is string locator)
        {
            using var document = JsonDocument.Parse(locator);
            string root = document.RootElement.GetProperty("Root").GetString() ?? throw new IOException("Workspace root is missing.");
            Require(Path.IsPathFullyQualified(root), "Workspace root is not absolute."); config = Path.Combine(root, "config.toml");
        }
        if (exists && value is not string) throw new IOException("Workspace locator has an unexpected registry value type.");
        try { using var file = ProtectedFile.Open(config!); hash = file.Hash(); } catch (FileNotFoundException) { }
        return new(exists, kind, raw, config, hash);
    }
    private static void VerifyDataUnchanged(DataBackup expected) => Require(ReadData() == expected, "Workspace locator/configuration changed; the harness never restores or overwrites user configuration.");
    private static List<Process> SnapshotOriginalProcesses(Audit audit)
    {
        var processes = Process.GetProcessesByName("CursorShake").Concat(Process.GetProcessesByName("CursorShake.RecoveryGuard")).ToList();
        try { foreach (var process in processes) { _ = process.SafeHandle; audit.OriginalProcesses.Add(new(process.Id, process.StartTime.ToUniversalTime().Ticks, process.MainModule!.FileName)); } return processes; }
        catch { foreach (var process in processes) process.Dispose(); throw; }
    }
    private static IEnumerable<ProcessProof> OwnProcesses(Audit audit)
    {
        foreach (var process in Process.GetProcesses())
        {
            ProcessProof? owned = null;
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        long start = process.StartTime.ToUniversalTime().Ticks;
                        string? path = process.MainModule?.FileName;
                        if (!process.HasExited && (Inside(path ?? "", audit.Root) || Inside(path ?? "", audit.App))) owned = new(process.Id, start, path!);
                    }
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
            if (owned is not null) yield return owned;
        }
    }
    private static IEnumerable<ProcessProof> LiveOwnedProcesses(Audit audit)
    {
        if (audit.AllUsers) return LiveMachineProcesses(audit);
        return LivePerUserProcesses(audit);
    }
    private static IEnumerable<ProcessProof> LivePerUserProcesses(Audit audit)
    {
        // WAIT binds the process lifetime by PID+StartTicks. Loader/clone module paths may change
        // or become unreadable during shutdown; those observations never grant popup authority.
        var seen = new HashSet<(int Pid, long StartTicks)>();
        foreach (var helper in OwnProcesses(audit))
            if (!audit.KnownHelpers.Any(item => item.Pid == helper.Pid && item.StartTicks == helper.StartTicks)) audit.KnownHelpers.Add(helper);
        foreach (var known in audit.Phases.Select(phase => new ProcessProof(phase.Pid, phase.StartTicks, phase.Executable)).Concat(audit.KnownHelpers).ToArray())
        {
            if (!seen.Add((known.Pid, known.StartTicks))) continue;
            Process? process;
            try { process = Process.GetProcessById(known.Pid); } catch (ArgumentException) { continue; }
            using (process)
            {
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != known.StartTicks) continue;
                string observed;
                try { observed = process.MainModule?.FileName ?? "<module unavailable>"; }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                { if (process.HasExited) continue; observed = "<module unavailable: " + error.Message + ">"; }
                if (process.HasExited) continue;
                string observation = $"PID={known.Pid}; StartTicks={known.StartTicks}; expected/origin={known.Executable}; observed={observed}";
                if (!audit.ProcessPathObservations.Contains(observation, StringComparer.Ordinal)) audit.ProcessPathObservations.Add(observation);
                yield return new(known.Pid, known.StartTicks, observed);
            }
        }
    }
    private sealed record TrackedProcess(SafeProcessHandle Handle, ProcessProof Proof, string Origin);
    private static TrackedProcess? ObserveMachineProcess(int pid, bool required, string origin)
    {
        var handle = Native.OpenProcess(0x00100000 | 0x1000, false, (uint)pid);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error == 87 || !required) return null;
            throw new IOException($"Cannot bind owned process {pid}; quiescence is unproven.", new System.ComponentModel.Win32Exception(error));
        }
        try
        {
            if (!Native.GetProcessTimes(handle, out long created, out _, out _, out _)) throw new IOException("Cannot read owned process creation time.");
            long ticks = DateTime.FromFileTimeUtc(created).Ticks;
            uint state = Native.WaitForSingleObject(handle, 0);
            Require(state is 0 or 258, "Cannot determine owned process lifetime.");
            string image = "<exited before image observation>";
            if (state == 258)
            {
                var path = new StringBuilder(32768); uint size = (uint)path.Capacity;
                if (!Native.QueryFullProcessImageName(handle, 0, path, ref size)) throw new IOException($"Cannot query owned process image {pid}.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                image = NormalizePath(path.ToString());
            }
            return new(handle, new(pid, ticks, image), origin);
        }
        catch when (!required) { handle.Dispose(); return null; }
        catch { handle.Dispose(); throw; }
    }
    private static bool MachineAlive(TrackedProcess process)
    {
        uint state = Native.WaitForSingleObject(process.Handle, 0);
        Require(state is 0 or 258, "Owned process handle cannot prove quiescence."); return state == 258;
    }
    private static void TrackMachineProcess(Audit audit, int pid, long expectedStart, string expectedImage, string origin)
    {
        if (audit.Tracked.ContainsKey(pid)) return;
        TrackedProcess? observed;
        try { observed = ObserveMachineProcess(pid, required: true, origin); }
        catch { audit.ProcessTrackingUnproven = true; throw; }
        if (observed is null) { audit.ProcessTrackingUnproven = true; throw new IOException("Started phase lifetime could not be bound; original-task restoration is blocked."); }
        try
        {
            Require(observed.Proof.StartTicks == expectedStart, "Started process identity differs from the recorded lifetime.");
            if (MachineAlive(observed)) Require(Same(observed.Proof.Executable, expectedImage), "Started process image differs from its verified executable.");
            audit.Tracked.Add(pid, observed);
        }
        catch { observed.Handle.Dispose(); audit.ProcessTrackingUnproven = true; throw; }
    }
    private static IEnumerable<ProcessProof> LiveMachineProcesses(Audit audit)
    {
        Require(!audit.ProcessTrackingUnproven, "A started installer process lifetime could not be verified; preserve the task backup and wait for manual resolution.");
        foreach(var phase in audit.Phases)
            if(!audit.Tracked.ContainsKey(phase.Pid))TrackMachineProcess(audit,phase.Pid,phase.StartTicks,phase.Executable,"recorded phase");
        var parents = new Dictionary<int,int>();
        using(var snapshot = Native.CreateToolhelp32Snapshot(2, 0))
        {
            Require(!snapshot.IsInvalid, "Process-tree snapshot is unavailable; quiescence is unproven.");
            var entry = new Native.ProcessEntry { Size = (uint)Marshal.SizeOf<Native.ProcessEntry>() };
            if(Native.Process32First(snapshot, ref entry)) do { parents[(int)entry.Pid] = (int)entry.ParentPid; } while(Native.Process32Next(snapshot, ref entry));
        }
        long earliest = audit.Phases.Count == 0 ? long.MaxValue : audit.Phases.Min(p => p.StartTicks);
        // UAC may re-parent the elevated Setup wrapper. A fresh process executing the exact pinned
        // candidate is an observe-only bridge; it never grants authority to close a window/process.
        foreach(int pid in parents.Keys)
        {
            if(audit.Tracked.ContainsKey(pid))continue;
            var observed = ObserveMachineProcess(pid, required: false, "owned path or exact candidate UAC bridge");
            if(observed is null)continue;
            string image = observed.Proof.Executable;
            if(observed.Proof.StartTicks >= earliest && (Inside(image, audit.Root) || Inside(image, audit.App) || Same(image, audit.Candidate))) audit.Tracked.Add(pid, observed);
            else observed.Handle.Dispose();
        }
        bool changed;
        do
        {
            changed = false;
            foreach(var pair in parents)
            {
                if(audit.Tracked.ContainsKey(pair.Key) || !audit.Tracked.TryGetValue(pair.Value, out var parent))continue;
                TrackedProcess? child;
                try { child = ObserveMachineProcess(pair.Key, required: true, "descendant of " + pair.Value); }
                catch { audit.ProcessTrackingUnproven = true; throw; }
                if(child is null)
                {
                    audit.ProcessTrackingUnproven = true;
                    throw new IOException("An observed descendant exited before its lifetime could be bound; preserve the backup until manual quiescence verification.");
                }
                if(child.Proof.StartTicks < parent.Proof.StartTicks){child.Handle.Dispose();audit.ProcessTrackingUnproven = true;throw new IOException("Descendant creation predates its held parent identity.");}
                audit.Tracked.Add(pair.Key, child);changed = true;
            }
        }while(changed);
        var alive = new List<ProcessProof>();
        foreach(var tracked in audit.Tracked.Values)
        {
            string note = $"PID={tracked.Proof.Pid}; StartTicks={tracked.Proof.StartTicks}; image={tracked.Proof.Executable}; origin={tracked.Origin}";
            if(!audit.ProcessPathObservations.Contains(note, StringComparer.Ordinal))audit.ProcessPathObservations.Add(note);
            if(MachineAlive(tracked))alive.Add(tracked.Proof);
        }
        return alive;
    }
    private static void CaptureMachinePopups(Audit audit, PhaseProof phase, HashSet<nint> seen)
    {
        _ = LiveMachineProcesses(audit).ToArray(); Exception? failure = null;
        Native.EnumWindows((window, _) =>
        {
            try
            {
                if(!Native.IsWindowVisible(window)||seen.Contains(window))return true;
                Native.GetWindowThreadProcessId(window, out uint pid);
                if(!audit.Tracked.TryGetValue((int)pid, out var owner)||!MachineAlive(owner))return true;
                string kind = Native.Class(window);var texts = new List<string>();bool interactive = false;
                Native.EnumChildWindows(window, (child, _) =>
                {
                    string text = Native.Text(child);if(text.Length>0)texts.Add(text);
                    string type = Native.Class(child);
                    if(Native.IsWindowVisible(child)&&(type.Contains("BUTTON",StringComparison.OrdinalIgnoreCase)||type.Contains("EDIT",StringComparison.OrdinalIgnoreCase)||type.Contains("COMBOBOX",StringComparison.OrdinalIgnoreCase)))interactive=true;
                    return true;
                },0);
                seen.Add(window);if(kind=="TWindowDisabler-Window"&&!interactive)return true;
                phase.Popups.Add(new{Hwnd=window.ToInt64(),Pid=pid,Executable=owner.Proof.Executable,Title=Native.Text(window),Class=kind,Text=texts,Action="Recorded only; no cross-integrity close message was sent."});
                Write(Path.Combine(audit.Root,phase.Name+".popup-evidence.json"),phase.Popups);
                return true;
            }
            catch(Exception error){failure=error;return false;}
        },0);
        if(failure is not null)throw new IOException("Cannot verify an owned elevated popup.",failure);
    }
    private static string FindProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "installer", "CursorShake.iss"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Project root not found.");
    }
    private static bool Inside(string path, string parent) => !string.IsNullOrEmpty(path) && NormalizePath(path).StartsWith(NormalizePath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Same(string a, string b) => string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);
    private static string NormalizePath(string path)
    {
        string value = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        value = Path.GetFullPath(value);
        var expanded = new StringBuilder(32768);
        uint length = Native.GetLongPathNameW(value, expanded, (uint)expanded.Capacity);
        return Path.TrimEndingDirectorySeparator(length > 0 && length < expanded.Capacity ? expanded.ToString() : value);
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void Write(string path, object value)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, Json); stream.Flush(flushToDisk: true);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }

    private sealed class Audit
    {
        public string Candidate { get; set; } = ""; public string? CandidateHash { get; set; } public string Report { get; set; } = "";
        public string ExpectedManifestHash { get; set; } = ""; public int ExpectedPayloadFiles { get; set; }
        public string Root { get; set; } = ""; public string App { get; set; } = ""; public string Temp { get; set; } = ""; public string Index { get; set; } = ""; public string Sid { get; set; } = "";
        public bool AllUsers { get; init; }
        public bool ProcessTrackingUnproven { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public Dictionary<int, TrackedProcess> Tracked { get; } = [];
        public bool Passed { get; set; } public bool RoundtripCompleted { get; set; } public bool Uninstalled { get; set; } public bool OriginalTaskRestored { get; set; } public bool DataUnchanged { get; set; }
        public bool SetupExecuted => Phases.Count > 0; public bool DataDeleteRequested => false; public bool AnyProcessForciblyKilled => false;
        public List<ProcessProof> KnownHelpers { get; } = []; public List<string> ProcessPathObservations { get; } = [];
        public List<string> Errors { get; } = []; public List<PhaseProof> Phases { get; } = []; public List<int> InstalledPayloadCounts { get; } = []; public List<ProcessProof> OriginalProcesses { get; } = []; public ProcessProof[] RemainingQaProcesses { get; set; } = [];
    }
    private sealed class PhaseProof
    {
        public string Name { get; init; } = ""; public int Pid { get; init; } public long StartTicks { get; init; } public string Executable { get; init; } = ""; public string[] Arguments { get; init; } = [];
        public int? ExitCode { get; set; } public bool ProcessStillRunning { get; set; } public List<object> Popups { get; } = [];
    }
    private sealed record ProcessProof(int Pid, long StartTicks, string Executable);
    private sealed record UninstallProof(string Hive, string View, string Location, string UninstallCommand);
    private sealed record DataBackup(bool WorkspaceExists, string? ValueKind, string RawJson, string? ConfigPath, string? ConfigHash);
    private sealed record TaskBackup(string Name, string Xml, string Sddl, bool Enabled);

    private sealed class Scheduler : IDisposable
    {
        private readonly object service;
        private readonly object folder;
        private readonly string sid;
        private readonly string name;
        private TaskBackup? expectedQaTask;
        internal Scheduler(string sid)
        {
            this.sid = sid; name = "CursorShake-" + sid;
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new PlatformNotSupportedException("Task Scheduler is unavailable."))!;
            try { ((dynamic)service).Connect(); folder = ((dynamic)service).GetFolder("\\"); }
            catch { Release(service); throw; }
        }
        internal TaskBackup? Read()
        {
            object task;
            try { task = ((dynamic)folder).GetTask(name); }
            catch (Exception error) when ((uint)error.HResult == 0x80070002) { return null; }
            try { return new((string)((dynamic)task).Name, (string)((dynamic)task).Xml, (string)((dynamic)task).GetSecurityDescriptor(7), (bool)((dynamic)task).Enabled); }
            finally { Release(task); }
        }
        internal void AssertRestorable(TaskBackup task)
        {
            Require(task.Name.Equals(name, StringComparison.OrdinalIgnoreCase), "Unexpected startup task name.");
            var parsed = InstalledStartupTasks.ParseDefinition(task.Name, task.Xml);
            Require(parsed.Source == TaskSource && StartupRegistration.PrincipalMatchesSid(parsed.Principal, sid), "Existing startup task has foreign source/principal; refusing mutation.");
            Require(parsed.Actions.Count == 1 && parsed.Actions[0].IsExec && string.IsNullOrEmpty(parsed.Actions[0].Arguments), "Existing startup task cannot be safely restored.");
            Require(parsed.Actions[0].Executable is { } path && Path.IsPathFullyQualified(path) && Path.GetFileName(path).Equals("CursorShake.exe", StringComparison.OrdinalIgnoreCase), "Existing startup task does not launch a CursorShake executable.");
            var xml = XDocument.Parse(task.Xml); XNamespace ns = xml.Root!.Name.Namespace;
            var principal = xml.Root.Element(ns + "Principals")!.Elements(ns + "Principal").Single();
            Require((string?)principal.Element(ns + "LogonType") == "InteractiveToken" && ((string?)principal.Element(ns + "RunLevel") ?? "LeastPrivilege") == "LeastPrivilege", "Original task cannot be restored safely without credentials/elevation.");
            var descriptor = new RawSecurityDescriptor(task.Sddl);
            using var identity = WindowsIdentity.GetCurrent();
            var currentSid=identity.User??throw new IOException("Current SID is unavailable.");
            var primaryGroup=descriptor.Group??throw new IOException("Task primary group is unavailable.");
            Require(descriptor.Owner is not null && descriptor.Owner.Equals(currentSid), "Original task owner cannot be restored safely by this non-elevated user.");
            Require(primaryGroup.Equals(currentSid) || identity.Groups?.OfType<SecurityIdentifier>().Any(group => group.Equals(primaryGroup)) == true, "Original task group is not present in the current token; refusing mutation.");
            Require(SimpleAllowAcl(descriptor), "Complex, callback, object or deny task ACL cannot be normalized safely; refusing mutation.");
        }
        internal void AssertQaTask(TaskBackup task, string qaRoot)
        {
            AssertRestorable(task);
            var parsed = InstalledStartupTasks.ParseDefinition(task.Name, task.Xml);
            Require(Same(parsed.Actions[0].Executable!, Path.Combine(qaRoot, "CursorShake.exe")), "Current task belongs to another installation; preserving it.");
        }
        internal void ObserveExpectedQaTask(TaskBackup task, string qaRoot)
        {
            AssertQaTask(task, qaRoot); expectedQaTask = task;
        }
        internal void Restore(TaskBackup? original, string qaRoot)
        {
            var current = Read();
            if (Equivalent(current, original)) return;
            if (current is not null)
            {
                AssertQaTask(current, qaRoot);
                Require(expectedQaTask is not null && Equivalent(current, expectedQaTask), "Startup task changed outside the captured QA mutation; backup preserved instead of overwriting it.");
            }
            // Re-read immediately before writing; no blind overwrite of an intervening registration.
            Require(Equivalent(current, Read()), "Startup task changed during restoration preflight.");
            if (original is null)
            {
                if (current is not null) ((dynamic)folder).DeleteTask(name, 0);
                Require(Read() is null, "QA startup task remains."); return;
            }
            AssertRestorable(original);
            const int createOrUpdateWithoutPrincipalAce = 22;
            object restored = ((dynamic)folder).RegisterTask(name, original.Xml, createOrUpdateWithoutPrincipalAce, sid, null, 3, original.Sddl);
            Release(restored);
            // Explicit SDDL adds RegistrationInfo.SecurityDescriptor to XML. Update the original
            // definition without a descriptor to remove that node while preserving the restored ACL.
            object definitionRestored = ((dynamic)folder).RegisterTask(name, original.Xml, createOrUpdateWithoutPrincipalAce, sid, null, 3, null);
            Release(definitionRestored);
            Require(Equivalent(original, Read()), "Restored task XML/security/enabled state differs from its original backup.");
        }
        private static bool Equivalent(TaskBackup? left, TaskBackup? right)
        {
            if (left is null || right is null) return left is null && right is null;
            return left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase) && left.Enabled == right.Enabled
                && string.Equals(left.Xml, right.Xml, StringComparison.Ordinal)
                && SecurityEquivalent(left.Sddl, right.Sddl);
        }
        private static bool SimpleAllowAcl(RawSecurityDescriptor security) => security.DiscretionaryAcl is not null
            && security.DiscretionaryAcl.Cast<GenericAce>().All(ace => ace is CommonAce common
                && common.AceQualifier == AceQualifier.AccessAllowed && !common.IsCallback && common.OpaqueLength == 0);
        private static bool SecurityEquivalent(string expected, string actual)
        {
            if (SecurityBytes(expected).AsSpan().SequenceEqual(SecurityBytes(actual))) return true;
            var left = new RawSecurityDescriptor(expected); var right = new RawSecurityDescriptor(actual);
            if (!SimpleAllowAcl(left) || !SimpleAllowAcl(right)) return false;
            if (!Equals(left.Owner, right.Owner) || !Equals(left.Group, right.Group)) return false;
            // Only DACL ACE order and the AI bit are nonsemantic for this restricted all-Allow ACL.
            const ControlFlags ignored = ControlFlags.DiscretionaryAclAutoInherited;
            if ((left.ControlFlags & ~ignored) != (right.ControlFlags & ~ignored)) return false;
            if (!AclBytes(left.SystemAcl).AsSpan().SequenceEqual(AclBytes(right.SystemAcl))) return false;
            static string[] Rules(RawSecurityDescriptor descriptor) => descriptor.DiscretionaryAcl!.Cast<CommonAce>()
                .Select(ace => $"{ace.SecurityIdentifier.Value}|{ace.AccessMask}|{(int)ace.AceFlags}")
                .Order(StringComparer.Ordinal).ToArray();
            return Rules(left).SequenceEqual(Rules(right)); // Multiset comparison preserves duplicate ACE counts and ALL AceFlags.
        }
        private static byte[] AclBytes(RawAcl? acl) { if (acl is null) return []; byte[] bytes = new byte[acl.BinaryLength]; acl.GetBinaryForm(bytes, 0); return bytes; }
        private static byte[] SecurityBytes(string sddl) { var security = new RawSecurityDescriptor(sddl); byte[] bytes = new byte[security.BinaryLength]; security.GetBinaryForm(bytes, 0); return bytes; }
        public void Dispose() { try { Release(folder); } finally { Release(service); } }
        private static void Release(object value) { if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    }

    private static void ExecutePhase(Audit audit, string phase, string executable, string[] arguments)
    {
        bool shellElevation = audit.AllUsers && phase.StartsWith("uninstall", StringComparison.Ordinal);
        var start = new ProcessStartInfo(executable) { UseShellExecute = shellElevation, CreateNoWindow = !shellElevation, WindowStyle = shellElevation ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden };
        if (shellElevation) start.Verb = "runas";
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        // Child-only TEMP contains extracted helpers, so popup ownership can be verified by exact private paths.
        // No parent/global environment variable or Windows setting is changed.
        if (!shellElevation) { start.Environment["TEMP"] = audit.Temp; start.Environment["TMP"] = audit.Temp; }
        using var process = Process.Start(start) ?? throw new IOException("Cannot start QA " + phase);
        _ = process.SafeHandle;
        var proof = new PhaseProof { Name = phase, Pid = process.Id, StartTicks = process.StartTime.ToUniversalTime().Ticks, Executable = executable, Arguments = arguments };
        audit.Phases.Add(proof);
        if (audit.AllUsers) TrackMachineProcess(audit, proof.Pid, proof.StartTicks, executable, "started phase");
        var watch = Stopwatch.StartNew(); var seen = new HashSet<nint>(); int quietScans=0;
        while (true)
        {
            CaptureKnownOwnPopups(audit, proof, process, seen);
            bool ownHelpersAlive = LiveOwnedProcesses(audit).Any();
            if (process.HasExited && !ownHelpersAlive) { if(!audit.AllUsers || ++quietScans >= 2)break; } else quietScans=0;
            Thread.Sleep(100); // Bounded integration polling; no user process is signaled or killed.
            if (watch.Elapsed >= TimeSpan.FromSeconds(audit.AllUsers ? 300 : 90))
            {
                proof.ProcessStillRunning = !process.HasExited || ownHelpersAlive;
                throw new TimeoutException($"QA {phase} exceeded its bounded wait. Its own process {process.Id} was not forcibly terminated; preserve logs/state.");
            }
        }
        proof.ExitCode = process.ExitCode;
        Require(proof.Popups.Count == 0, "Unexpected own installer/helper popup was captured; inspect evidence before continuing.");
        Require(process.ExitCode == 0, $"QA {phase} exited with code {process.ExitCode}.");
    }
    private static void CaptureKnownOwnPopups(Audit audit, PhaseProof proof, Process started, HashSet<nint> seen)
    {
        if (audit.AllUsers) { CaptureMachinePopups(audit, proof, seen); return; }
        Native.EnumWindows((window, _) =>
        {
            if (!Native.IsWindowVisible(window) || seen.Contains(window)) return true;
            Native.GetWindowThreadProcessId(window, out uint pid);
            if (pid == 0) return true;
            string? path = null;
            try
            {
                using var owner = Process.GetProcessById((int)pid);
                if (owner.StartTime.ToUniversalTime().Ticks < proof.StartTicks) return true;
                path = owner.MainModule?.FileName;
                if (owner.Id != started.Id && !Inside(path ?? "", audit.Root)) return true;
                if (owner.Id == started.Id && !Same(path ?? "", proof.Executable)) return true;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
            string title = Native.Text(window), kind = Native.Class(window);
            var childText = new List<string>(); var okButtons = new List<nint>(); bool interactiveChildren = false;
            Native.EnumChildWindows(window, (child, _) =>
            {
                string text = Native.Text(child); if (text.Length > 0) childText.Add(text);
                string childClass = Native.Class(child);
                if (Native.IsWindowVisible(child) && (childClass.Contains("BUTTON", StringComparison.OrdinalIgnoreCase) || childClass.Contains("EDIT", StringComparison.OrdinalIgnoreCase) || childClass.Contains("COMBOBOX", StringComparison.OrdinalIgnoreCase))) interactiveChildren = true;
                if (kind == "#32770" && Native.Class(child) == "Button" && Native.GetDlgCtrlID(child) == 1 && text.Replace("&", "").Trim() is "OK" or "Aceptar") okButtons.Add(child);
                return true;
            }, 0);
            if (kind == "TWindowDisabler-Window" && !interactiveChildren) { seen.Add(window); return true; }
            seen.Add(window);
            string log = Path.Combine(audit.Root, proof.Name.StartsWith("uninstall", StringComparison.Ordinal) ? "uninstall.log" : proof.Name + ".log");
            string? tail = null;
            try { using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var reader = new StreamReader(stream); string all = reader.ReadToEnd(); tail = all.Length > 16000 ? all[^16000..] : all; }
            catch (IOException) { }
            proof.Popups.Add(new { Hwnd = window.ToInt64(), Pid = pid, Executable = path, Title = title, Class = kind, Text = childText, LogTailBeforeClose = tail });
            Write(Path.Combine(audit.Root, proof.Name + ".popup-evidence.json"), proof.Popups); // Persist evidence before sending any close message.
            if (Inside(path ?? "", audit.Root) && string.Equals(Path.GetFileName(path), "CursorShake.exe", StringComparison.OrdinalIgnoreCase) && title.StartsWith("CursorShake —", StringComparison.Ordinal))
                Native.PostMessage(window, 0x10, 0, 0); // Only the own helper's diagnostic form; never an arbitrary app/guard.
            else if (kind == "#32770" && okButtons.Count == 1)
                Native.PostMessage(window, 0x111, 1, okButtons[0]); // Acknowledge only a known native OK error dialog.
            return true;
        }, 0);
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]internal struct ProcessEntry
        {public uint Size,Usage,Pid;public nuint Heap;public uint Module,Threads,ParentPid;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Exe;}
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern SafeProcessHandle OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint pid);
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool GetProcessTimes(SafeProcessHandle process,out long creation,out long exit,out long kernel,out long user);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint WaitForSingleObject(SafeProcessHandle process,uint milliseconds);
        [DllImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref uint size);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags,uint pid);
        [DllImport("kernel32.dll",EntryPoint="Process32FirstW",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool Process32First(SafeFileHandle snapshot,ref ProcessEntry entry);
        [DllImport("kernel32.dll",EntryPoint="Process32NextW",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool Process32Next(SafeFileHandle snapshot,ref ProcessEntry entry);
        internal delegate bool EnumWindow(nint window, nint state);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumWindow callback, nint state);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint state);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
        [DllImport("user32.dll")] internal static extern int GetDlgCtrlID(nint window);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint maximum);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint window, StringBuilder text, int maximum);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder text, int maximum);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
        internal static string Text(nint window) { var text = new StringBuilder(4096); GetWindowTextW(window, text, text.Capacity); return text.ToString(); }
        internal static string Class(nint window) { var text = new StringBuilder(256); GetClassNameW(window, text, text.Capacity); return text.ToString(); }
    }

}
