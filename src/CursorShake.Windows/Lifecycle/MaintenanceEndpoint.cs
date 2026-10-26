using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace CursorShake.Windows.Lifecycle;

/// <summary>Local shutdown request only. The callback must marshal onto the application's UI thread.</summary>
public sealed class MaintenanceEndpoint : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly NamedPipeServerStream server;
    private bool disposed;
    public Task Completion { get; }
    public static string NameFor(int processId, long startTicks) => $"CursorShake.Maintenance.{processId}.{startTicks}";
    public MaintenanceEndpoint(Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(requestShutdown);
        using var current = Process.GetCurrentProcess();
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        server = NamedPipeServerStreamAcl.Create(NameFor(current.Id, current.StartTime.ToUniversalTime().Ticks), PipeDirection.InOut, 1, PipeTransmissionMode.Message,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 128, 128, security);
        Completion = Run(requestShutdown);
    }
    private async Task Run(Action shutdown)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await server.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                try
                {
                    if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out uint client) || client == 0) throw new IOException("A local maintenance client could not be verified.");
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var request = new byte[16]; int length = await server.ReadAsync(request, timeout.Token).ConfigureAwait(false);
                    if (length != 4 || !server.IsMessageComplete || !request.AsSpan(0, 4).SequenceEqual("STOP"u8)) continue;
                    await server.WriteAsync("OK"u8.ToArray(), timeout.Token).ConfigureAwait(false);
                    // Disconnect discards unread pipe data. Wait for the client's acknowledgement
                    // before invoking a callback that may dispose this endpoint immediately.
                    length = await server.ReadAsync(request, timeout.Token).ConfigureAwait(false);
                    if (length == 3 && server.IsMessageComplete && request.AsSpan(0, 3).SequenceEqual("ACK"u8)) shutdown();
                }
                catch (Exception e) when (e is IOException or OperationCanceledException) { }
                finally { if (server.IsConnected) server.Disconnect(); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stopping.IsCancellationRequested) { }
    }
    public static async Task RequestShutdownAndWaitAsync(Process target, CancellationToken cancellationToken = default)
    {
        // Reading StartTime and opening the handle pins the exact process identity for peer verification and exit.
        int pid = target.Id; long start = target.StartTime.ToUniversalTime().Ticks; _ = target.SafeHandle;
        if (target.HasExited) { RequireCleanExit(target); return; }
        try
        {
            using var pipe = new NamedPipeClientStream(".", NameFor(pid, start), PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous);
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); connect.CancelAfter(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(connect.Token).ConfigureAwait(false); pipe.ReadMode = PipeTransmissionMode.Message;
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid) || serverPid != pid || target.HasExited) throw new IOException("Maintenance endpoint does not belong to the target process.");
            await pipe.WriteAsync("STOP"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            var response = new byte[16]; int length = await pipe.ReadAsync(response, cancellationToken).ConfigureAwait(false);
            if (length != 2 || !pipe.IsMessageComplete || !response.AsSpan(0, 2).SequenceEqual("OK"u8)) throw new IOException("CursorShake did not acknowledge a clean shutdown request.");
            await pipe.WriteAsync("ACK"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException)
        {
            if (target.HasExited) { RequireCleanExit(target); return; }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new IOException("CursorShake could not be closed through its maintenance endpoint. Exit it from the tray menu and run setup again.", e);
        }
        await target.WaitForExitAsync(cancellationToken).ConfigureAwait(false); RequireCleanExit(target);
    }
    internal static void RequireCleanExit(Process target)
    { if (target.ExitCode != 0) throw new IOException($"CursorShake process {target.Id} exited with code {target.ExitCode}; maintenance was stopped."); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; stopping.Cancel(); server.Dispose();
        _ = Completion.ContinueWith(_ => stopping.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        // Completion can be observed by the host; cancellation does not block the UI on its own callback.
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
}
