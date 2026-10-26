using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using CursorShake.Windows.Lifecycle;

namespace CursorShake.NativeSmoke;

internal static class StartupRegistrationSmoke
{
    internal static int Run(string output)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("NativeSmoke executable is unavailable.");
        string taskName = "CursorShake-QA-" + Guid.NewGuid().ToString("N");
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("SID is unavailable.");
        var registration = new StartupRegistration(executable, taskName);
        string? failure = null, cleanupFailure = null, xml = null;
        bool initiallyAbsent = false, enabled = false, xmlVerified = false, disabledAfterCleanup = false, absentAfterCleanup = false;
        try
        {
            initiallyAbsent = ReadXml(taskName) is null && !registration.IsEnabled();
            Need(initiallyAbsent, "Unique QA task must not exist initially.");
            registration.SetEnabled(true);
            enabled = registration.IsEnabled();
            Need(enabled, "New startup task must be enabled.");
            xml = ReadXml(taskName) ?? throw new InvalidOperationException("Created task was not found.");
            var doc = XDocument.Parse(xml); XNamespace ns = doc.Root!.Name.Namespace;
            var principal = doc.Root.Element(ns + "Principals")!.Element(ns + "Principal")!;
            Need((string?)principal.Element(ns + "UserId") == sid, "Startup principal SID differs.");
            Need((string?)principal.Element(ns + "LogonType") == "InteractiveToken", "Startup must use InteractiveToken.");
            Need(((string?)principal.Element(ns + "RunLevel") ?? "LeastPrivilege") == "LeastPrivilege", "Startup must use least privilege.");
            Need((string?)doc.Root.Element(ns + "RegistrationInfo")?.Element(ns + "Source") == "CursorShake", "Startup source ownership differs.");
            var settings = doc.Root.Element(ns + "Settings")!;
            Need((string?)settings.Element(ns + "ExecutionTimeLimit") == "PT0S", "Long-running utility must have no execution timeout.");
            Need((string?)settings.Element(ns + "MultipleInstancesPolicy") == "IgnoreNew", "Startup must ignore duplicate instances.");
            Need((string?)settings.Element(ns + "DisallowStartIfOnBatteries") == "false" && (string?)settings.Element(ns + "StopIfGoingOnBatteries") == "false", "Battery use must be allowed.");
            var trigger = doc.Root.Element(ns + "Triggers")!.Elements().Single();
            Need(trigger.Name == ns + "LogonTrigger" && ResolveSid((string?)trigger.Element(ns + "UserId")) == sid, "Exactly one per-user logon trigger is required.");
            Need(((string?)trigger.Element(ns + "Delay") ?? "PT0S") == "PT0S", "Logon trigger must have no configured delay.");
            var action = doc.Root.Element(ns + "Actions")!.Elements().Single();
            Need(action.Name == ns + "Exec", "Exactly one executable action is required.");
            Need(string.Equals((string?)action.Element(ns + "Command"), executable, StringComparison.OrdinalIgnoreCase), "Task command differs from the own test executable.");
            Need(string.IsNullOrEmpty((string?)action.Element(ns + "Arguments")), "Task must have no arguments; it must not trigger any QA mode.");
            Need(string.Equals((string?)action.Element(ns + "WorkingDirectory"), Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase), "Task working directory differs.");
            xmlVerified = true;
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            try
            {
                // Production API validates Source and principal SID before deleting this unique test-owned name.
                registration.SetEnabled(false);
                disabledAfterCleanup = !registration.IsEnabled();
                absentAfterCleanup = ReadXml(taskName) is null;
                Need(disabledAfterCleanup && absentAfterCleanup, "QA task cleanup was not verified.");
            }
            catch (Exception cleanup) { cleanupFailure = cleanup.ToString(); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string? xmlPath = xml is null ? null : Path.ChangeExtension(output, ".xml");
        if (xmlPath is not null) File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);
        bool passed = failure is null && cleanupFailure is null && initiallyAbsent && enabled && xmlVerified && absentAfterCleanup;
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = passed, TaskName = taskName, Executable = executable, TaskRunAttempted = false, LogonTestAttempted = false, SystemCursorMutation = false, InitiallyAbsent = initiallyAbsent, Enabled = enabled, XmlVerified = xmlVerified, DisabledAfterCleanup = disabledAfterCleanup, AbsentAfterCleanup = absentAfterCleanup, XmlPath = xmlPath, Failure = failure, CleanupFailure = cleanupFailure }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output); if (failure is not null) Console.Error.WriteLine(failure); if (cleanupFailure is not null) Console.Error.WriteLine(cleanupFailure);
        return passed ? 0 : 1;
    }

    private static string? ReadXml(string name)
    {
        var values = new List<object>();
        object Keep(object value) { values.Add(value); return value; }
        try
        {
            dynamic service = Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!);
            service.Connect(); dynamic folder = Keep(service.GetFolder("\\"));
            try { dynamic task = Keep(folder.GetTask(name)); return (string)task.Xml; }
            catch (Exception error) when ((uint)error.HResult == 0x80070002) { return null; }
        }
        finally { for (int i = values.Count - 1; i >= 0; i--) if (Marshal.IsComObject(values[i])) Marshal.FinalReleaseComObject(values[i]); }
    }
    private static string? ResolveSid(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return null;
        if (user.StartsWith("S-1-", StringComparison.Ordinal)) return new SecurityIdentifier(user).Value;
        return ((SecurityIdentifier)new NTAccount(user).Translate(typeof(SecurityIdentifier))).Value;
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
