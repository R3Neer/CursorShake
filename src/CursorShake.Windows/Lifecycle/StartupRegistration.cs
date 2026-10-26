using System.Runtime.InteropServices;
using System.Security.Principal;
using CursorShake.Windows.Configuration;
namespace CursorShake.Windows.Lifecycle;

/// <summary>Per-user logon registration. Construction and reading never create a task.</summary>
public sealed class StartupRegistration
{
    private readonly string _executablePath;
    private readonly string _sid;
    public string TaskName { get; }
    public StartupRegistration(string executablePath, string? taskName = null)
    {
        _executablePath = Path.GetFullPath(executablePath);
        _sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Interactive user SID is unavailable.");
        TaskName = taskName ?? "CursorShake-" + _sid;
    }
    public bool IsEnabled()
    {
        var com = new List<object>();
        try
        {
            dynamic folder = Folder(com); dynamic? task = Existing(folder, com);
            if (task is null) return false;
            RequireOwned(task, com);
            return (bool)task.Enabled;
        }
        finally { Release(com); }
    }
    public void SetEnabled(bool enabled)
    {
        var com = new List<object>();
        try
        {
            dynamic folder = Folder(com); dynamic? existing = Existing(folder, com);
            if (existing is not null) RequireOwned(existing, com);
            if (!enabled)
            {
                if (existing is not null) folder.DeleteTask(TaskName, 0);
                return;
            }
            if (!File.Exists(_executablePath)) throw new ConfigurationException("ExecutableMissing", "Startup executable does not exist.", _executablePath);
            dynamic service = com[0]; dynamic definition = Keep(com, service.NewTask(0));
            dynamic registration = Keep(com, definition.RegistrationInfo);
            registration.Description = "CursorShake cursor magnifier"; registration.Source = "CursorShake";
            dynamic principal = Keep(com, definition.Principal); principal.UserId = _sid; principal.LogonType = 3; principal.RunLevel = 0;
            dynamic settings = Keep(com, definition.Settings);
            settings.Enabled = true; settings.AllowDemandStart = true; settings.StartWhenAvailable = true;
            settings.DisallowStartIfOnBatteries = false; settings.StopIfGoingOnBatteries = false;
            settings.RunOnlyIfNetworkAvailable = false; settings.RunOnlyIfIdle = false;
            settings.ExecutionTimeLimit = "PT0S"; settings.MultipleInstances = 2;
            dynamic triggers = Keep(com, definition.Triggers); dynamic trigger = Keep(com, triggers.Create(9));
            trigger.UserId = _sid; trigger.Enabled = true; trigger.Delay = "PT0S";
            dynamic actions = Keep(com, definition.Actions); dynamic action = Keep(com, actions.Create(0));
            action.Path = _executablePath; action.Arguments = ""; action.WorkingDirectory = Path.GetDirectoryName(_executablePath);
            Keep(com, folder.RegisterTaskDefinition(TaskName, definition, 6, _sid, null, 3, null));
        }
        catch (COMException e) { throw new ConfigurationException("StartupRegistrationFailed", "Windows could not update per-user startup: " + e.Message, _executablePath, inner: e); }
        finally { Release(com); }
    }
    private dynamic Folder(List<object> com)
    {
        dynamic service = Keep(com, Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new PlatformNotSupportedException("Task Scheduler is unavailable."))!);
        service.Connect(); return Keep(com, service.GetFolder("\\"));
    }
    private dynamic? Existing(dynamic folder, List<object> com)
    {
        try { return Keep(com, folder.GetTask(TaskName)); }
        // COM projections can expose ERROR_FILE_NOT_FOUND as FileNotFoundException. Other scheduler failures are not absence.
        catch (Exception e) when ((uint)e.HResult == 0x80070002) { return null; }
    }
    private void RequireOwned(dynamic task, List<object> com)
    {
        dynamic definition = Keep(com, task.Definition); dynamic registration = Keep(com, definition.RegistrationInfo); dynamic principal = Keep(com, definition.Principal);
        if ((string?)registration.Source != "CursorShake" || !PrincipalMatchesSid((string?)principal.UserId, _sid)) throw new ConfigurationException("StartupConflict", "The startup task name belongs to another registration; it was not changed.");
    }
    internal static bool PrincipalMatchesSid(string? principal, string expectedSid)
    {
        if (string.IsNullOrWhiteSpace(principal)) return false;
        try
        {
            SecurityIdentifier identity;
            try { identity = new SecurityIdentifier(principal); }
            catch (ArgumentException) { identity = (SecurityIdentifier)new NTAccount(principal).Translate(typeof(SecurityIdentifier)); }
            return identity.Equals(new SecurityIdentifier(expectedSid));
        }
        catch (IdentityNotMappedException) { return false; }
        catch (ArgumentException) { return false; }
    }
    private static dynamic Keep(List<object> com, object value) { com.Add(value); return value; }
    private static void Release(List<object> com) { for (int i = com.Count - 1; i >= 0; i--) if (Marshal.IsComObject(com[i])) Marshal.FinalReleaseComObject(com[i]); }
}
