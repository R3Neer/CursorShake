using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using CursorShake.Windows.Lifecycle;
namespace CursorShake.Windows.Tests;

public sealed class InstallationMaintenanceTests
{
    [Fact] public void GateUsesCanonicalUtf16LittleEndianHashAndLowercaseHex()
    {
        string root = @"D:\Fixture\Résumé";
        string expected = @"Global\CursorShake.Installation." + Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(root.ToUpperInvariant()))).ToLowerInvariant();
        Assert.Equal(expected, InstallationGate.NameFor(root + "\\"));
        Assert.Equal(expected, InstallationGate.NameFor(@"d:\fixture\sub\..\résumé"));
    }
    [Fact] public void GateIsExclusiveAndRequiresItsAcquiringThreadForRelease()
    {
        string root = UniqueRoot(); using var held = InstallationGate.TryEnter(root, TimeSpan.Zero);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.ThrowsAny<IOException>(() => InstallationGate.TryEnter(root, TimeSpan.FromMilliseconds(25)));
                InstallationGate.AssertMaintenanceHeld(root);
                Assert.Throws<InvalidOperationException>(held.Dispose);
            }
            catch (Exception error) { failure = error; }
        });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.Throws<IOException>(() => InstallationGate.AssertMaintenanceHeld(root));
    }
    [Fact] public void GateAclExplicitlyAllowsUsersToSynchronizeAndModifyWithoutFullControl()
    {
        string root = UniqueRoot(); using var held = InstallationGate.TryEnter(root, TimeSpan.Zero);
        using var mutex = MutexAcl.OpenExisting(InstallationGate.NameFor(root), MutexRights.ReadPermissions | MutexRights.Synchronize);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var rules = mutex.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<MutexAccessRule>().ToArray();
        var rule = Assert.Single(rules, rule => rule.IdentityReference.Equals(users));
        Assert.Equal(MutexRights.Synchronize | MutexRights.Modify, rule.MutexRights);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
    }
    [Fact] public async Task MaintenanceWithoutInstallerGateIsRefusedBeforeAnyProcessScan()
        => await Assert.ThrowsAsync<IOException>(() => InstallationProcesses.QuiesceAsync(UniqueRoot()));
    [Theory]
    [InlineData("CursorShake", @"D:\App\CursorShake.exe", true)]
    [InlineData("CursorShake.RecoveryGuard", @"D:\App\CursorShake.RecoveryGuard.exe", true)]
    [InlineData("CursorShake", @"D:\Another\CursorShake.exe", false)]
    [InlineData("CursorShake", @"D:\App\Child\CursorShake.exe", false)]
    [InlineData("Other", @"D:\App\Other.exe", false)]
    public void ExecutableSelectionIsExactAndNeverTraversesOtherInstallations(string name, string executable, bool matches)
        => Assert.Equal(matches, InstallationProcesses.MatchesExecutable(@"D:\App", name, executable));
    [Theory] [InlineData("STOP")] [InlineData("STOPextra")] [InlineData("stop")] [InlineData("")]
    public async Task EndpointAcceptsOnlyItsExactStopMessage(string message)
    {
        int calls = 0; using var endpoint = new MaintenanceEndpoint(() => Interlocked.Increment(ref calls));
        using var current = Process.GetCurrentProcess();
        using(var client = new NamedPipeClientStream(".", MaintenanceEndpoint.NameFor(current.Id, current.StartTime.ToUniversalTime().Ticks), PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(timeout.Token); client.ReadMode = PipeTransmissionMode.Message;
            // Empty messages are represented by a different invalid command, not a zero-byte no-op write.
            await client.WriteAsync(Encoding.ASCII.GetBytes(message.Length == 0 ? "\n" : message), timeout.Token);
            byte[] ack = new byte[16]; int length = await client.ReadAsync(ack, timeout.Token);
            if (message == "STOP")
            {
                Assert.Equal("OK", Encoding.ASCII.GetString(ack, 0, length));
                await client.WriteAsync("ACK"u8.ToArray(), timeout.Token);
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, TimeSpan.FromSeconds(2)));
            }
            else { Assert.Equal(0, length); Assert.Equal(0, calls); }
        }
        endpoint.Dispose(); await endpoint.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact] public async Task EndpointRejectsNamespaceCollisionAndDisposesPendingAccept()
    {
        using var endpoint = new MaintenanceEndpoint(() => throw new InvalidOperationException());
        Assert.Throws<IOException>(() => new MaintenanceEndpoint(() => { }));
        endpoint.Dispose(); endpoint.Dispose(); await endpoint.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static string UniqueRoot() => Path.Combine(Path.GetTempPath(), "CursorShake.Gate.Tests", Guid.NewGuid().ToString("N"));
}
