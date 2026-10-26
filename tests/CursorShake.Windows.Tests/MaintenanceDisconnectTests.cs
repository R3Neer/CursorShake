using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using CursorShake.Windows.Lifecycle;
using Xunit;

namespace CursorShake.Windows.Tests;

// Endpoint names are process-wide; never overlap these fixtures with another test collection.
[CollectionDefinition("Maintenance disconnect isolation", DisableParallelization = true)]
public sealed class MaintenanceDisconnectCollection { }

[Collection("Maintenance disconnect isolation")]
public sealed class MaintenanceDisconnectTests
{
    [Theory]
    [InlineData("ConnectThenClose")]
    [InlineData("StopOkThenClose")]
    [InlineData("InvalidRequest")]
    [InlineData("InvalidAcknowledgement")]
    public async Task IncompleteClientCannotFaultTheListenerOrPreventTheNextValidStop(string scenario)
    {
        int calls = 0;
        using var called = new SemaphoreSlim(0);
        using var endpoint = new MaintenanceEndpoint(() => { Interlocked.Increment(ref calls); called.Release(); });
        try
        {
            using (var client = await Connect())
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                if (scenario != "ConnectThenClose")
                {
                    await client.WriteAsync(Encoding.ASCII.GetBytes(scenario == "InvalidRequest" ? "STOPextra" : "STOP"), timeout.Token);
                    byte[] reply = new byte[16]; int length = await client.ReadAsync(reply, timeout.Token);
                    if (scenario == "InvalidRequest") Assert.Equal(0, length);
                    else
                    {
                        Assert.Equal("OK", Encoding.ASCII.GetString(reply, 0, length));
                        if (scenario == "InvalidAcknowledgement")
                        {
                            await client.WriteAsync("BAD"u8.ToArray(), timeout.Token);
                            Assert.Equal(0, await client.ReadAsync(reply, timeout.Token));
                        }
                    }
                }
            }
            Assert.Equal(0, Volatile.Read(ref calls));
            AssertListenerHealthy(endpoint);
            await ValidStopAndCloseImmediately(endpoint, called);
            Assert.Equal(1, Volatile.Read(ref calls));
            AssertListenerHealthy(endpoint);
        }
        finally
        {
            endpoint.Dispose();
            await endpoint.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ClientDisposalImmediatelyAfterAckCannotLoseAnAdmittedShutdownOrPoisonTheNextConnection()
    {
        int calls = 0;
        using var called = new SemaphoreSlim(0);
        using var endpoint = new MaintenanceEndpoint(() => { Interlocked.Increment(ref calls); called.Release(); });
        try
        {
            for (int index = 1; index <= 32; index++)
            {
                await ValidStopAndCloseImmediately(endpoint, called);
                Assert.Equal(index, Volatile.Read(ref calls));
                AssertListenerHealthy(endpoint);
            }
        }
        finally
        {
            endpoint.Dispose();
            await endpoint.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task ValidStopAndCloseImmediately(MaintenanceEndpoint endpoint, SemaphoreSlim called)
    {
        try
        {
            using (var client = await Connect())
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await client.WriteAsync("STOP"u8.ToArray(), timeout.Token);
                byte[] reply = new byte[16]; int length = await client.ReadAsync(reply, timeout.Token);
                Assert.Equal("OK", Encoding.ASCII.GetString(reply, 0, length));
                await client.WriteAsync("ACK"u8.ToArray(), timeout.Token);
                // Match the production client: dispose immediately after WriteAsync, without flushing or extra acknowledgement.
            }
            Assert.True(await called.WaitAsync(TimeSpan.FromSeconds(3)), "The ACK was written, but shutdown was not admitted. Completion=" + Describe(endpoint));
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("The next valid client could not complete its handshake. Completion=" + Describe(endpoint));
        }
    }
    private static async Task<NamedPipeClientStream> Connect()
    {
        using var current = Process.GetCurrentProcess();
        var pipe = new NamedPipeClientStream(".", MaintenanceEndpoint.NameFor(current.Id, current.StartTime.ToUniversalTime().Ticks), PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(timeout.Token); pipe.ReadMode = PipeTransmissionMode.Message;
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
    }
    private static void AssertListenerHealthy(MaintenanceEndpoint endpoint)
        => Assert.False(endpoint.Completion.IsCompleted, "Listener terminated before disposal: " + Describe(endpoint));
    private static string Describe(MaintenanceEndpoint endpoint)
        => endpoint.Completion.Status + "; " + endpoint.Completion.Exception?.ToString();
}
