using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace CursorShake.Windows.Lifecycle;

/// <summary>Removes only verified per-user startup registrations for one exact installation.</summary>
public static class InstalledStartupTasks
{
    private const string Prefix = "CursorShake-";

    public static void RemoveForInstallation(string installationRoot)
    {
        string executable = InstalledExecutable(installationRoot);
        using var store = new SchedulerStore();
        RemoveForInstallation(Path.GetDirectoryName(executable)!, store);
    }

    internal static void RemoveForInstallation(string installationRoot, IInstalledStartupTaskStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _ = InstalledExecutable(installationRoot);
        var plan = Plan(installationRoot, store.ReadAll()); // Complete ownership validation before deleting anything.
        foreach (var candidate in plan)
        {
            var current = store.Read(candidate.Name);
            if (current is null) continue; // Another owner already removed the registration.
            Verify(installationRoot, candidate, current);
            store.Delete(candidate.Name);
        }
    }

    internal static IReadOnlyList<InstalledStartupRemoval> Plan(string installationRoot, IEnumerable<InstalledStartupTask> tasks)
    {
        string executable = InstalledExecutable(installationRoot);
        var result = new List<InstalledStartupRemoval>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            if (!task.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var paths = task.Actions.Select(action => action.IsExec ? CanonicalExecutable(action.Executable) : null).ToArray();
            bool pointsHere = paths.Any(path => string.Equals(path, executable, StringComparison.OrdinalIgnoreCase));
            if (!pointsHere)
            {
                if (paths.Length > 0 && paths.All(path => path is not null)) continue; // Clearly belongs to a different copy.
                throw Conflict(task.Name, "its installation cannot be determined from its actions");
            }
            if (!string.Equals(task.Source, "CursorShake", StringComparison.Ordinal)) throw Conflict(task.Name, "the registration Source differs");
            if (task.Actions.Count != 1 || !task.Actions[0].IsExec || !string.IsNullOrEmpty(task.Actions[0].Arguments))
                throw Conflict(task.Name, "exactly one Exec action without arguments is required");
            string sid = task.Name[Prefix.Length..];
            if (sid.Length == 0 || !IsSid(sid) || !StartupRegistration.PrincipalMatchesSid(task.Principal, sid))
                throw Conflict(task.Name, "the task name SID or principal identity cannot be verified");
            if (!names.Add(task.Name)) throw Conflict(task.Name, "the task name occurs more than once");
            result.Add(new(task.Name, Fingerprint(task)));
        }
        return result;
    }

    internal static void Verify(string installationRoot, InstalledStartupRemoval planned, InstalledStartupTask current)
    {
        if (!string.Equals(planned.Name, current.Name, StringComparison.OrdinalIgnoreCase) || planned.Fingerprint != Fingerprint(current))
            throw Conflict(planned.Name, "the definition changed after planning; nothing was deleted for this task");
        var accepted = Plan(installationRoot, [current]);
        if (accepted.Count != 1) throw Conflict(planned.Name, "the task no longer belongs to this installation");
    }

    internal static InstalledStartupTask ParseDefinition(string name, string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var document = XDocument.Load(reader);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var root = document.Root;
        if (root?.Name != ns + "Task") throw Conflict(name, "the Task Scheduler definition is invalid");
        var actions = root.Element(ns + "Actions")?.Elements().Select(action => new InstalledStartupTaskAction(action.Name == ns + "Exec", (string?)action.Element(ns + "Command"), (string?)action.Element(ns + "Arguments"))).ToArray() ?? [];
        var principals = root.Element(ns + "Principals")?.Elements(ns + "Principal").ToArray() ?? [];
        string? principal = principals.Length == 1 ? (string?)principals[0].Element(ns + "UserId") : null;
        return new(name, (string?)root.Element(ns + "RegistrationInfo")?.Element(ns + "Source"), principal, actions, xml);
    }

    private static string InstalledExecutable(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Installation root must be an absolute path.", nameof(root));
        return Path.GetFullPath(Path.Combine(Path.TrimEndingDirectorySeparator(root), "CursorShake.exe"));
    }
    private static string? CanonicalExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        try { return Path.GetFullPath(path); }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }
    private static bool IsSid(string value)
    {
        try { _ = new SecurityIdentifier(value); return true; }
        catch (ArgumentException) { return false; }
    }
    private static string Fingerprint(InstalledStartupTask task) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(task))));
    private static InvalidOperationException Conflict(string name, string reason) => new($"Startup task '{name}' was preserved because {reason}. Resolve this registration before removing the installation.");

    private sealed class SchedulerStore : IInstalledStartupTaskStore, IDisposable
    {
        private readonly object service;
        private readonly object folder;
        public SchedulerStore()
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new PlatformNotSupportedException("Task Scheduler is unavailable."))!;
            try { ((dynamic)service).Connect(); folder = ((dynamic)service).GetFolder("\\"); }
            catch { Release(service); throw; }
        }
        public IReadOnlyList<InstalledStartupTask> ReadAll()
        {
            object collection = ((dynamic)folder).GetTasks(1); // Include hidden registrations; the caller's existing permissions still apply.
            try
            {
                var result = new List<InstalledStartupTask>();
                int count = ((dynamic)collection).Count;
                for (int index = 1; index <= count; index++)
                {
                    object task = ((dynamic)collection)[index];
                    try
                    {
                        string name = ((dynamic)task).Name;
                        if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) result.Add(ParseDefinition(name, (string)((dynamic)task).Xml));
                    }
                    finally { Release(task); }
                }
                return result;
            }
            finally { Release(collection); }
        }
        public InstalledStartupTask? Read(string name)
        {
            object task;
            try { task = ((dynamic)folder).GetTask(name); }
            catch (Exception error) when ((uint)error.HResult == 0x80070002) { return null; }
            try { return ParseDefinition((string)((dynamic)task).Name, (string)((dynamic)task).Xml); }
            finally { Release(task); }
        }
        public void Delete(string name) => ((dynamic)folder).DeleteTask(name, 0);
        public void Dispose() { Release(folder); Release(service); }
        private static void Release(object value) { if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    }
}

internal sealed record InstalledStartupTaskAction(bool IsExec, string? Executable, string? Arguments);
internal sealed record InstalledStartupTask(string Name, string? Source, string? Principal, IReadOnlyList<InstalledStartupTaskAction> Actions, string DefinitionXml = "");
internal sealed record InstalledStartupRemoval(string Name, string Fingerprint);
internal interface IInstalledStartupTaskStore
{
    IReadOnlyList<InstalledStartupTask> ReadAll();
    InstalledStartupTask? Read(string name);
    void Delete(string name);
}
