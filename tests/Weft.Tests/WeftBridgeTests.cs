using Weft.Client;
using Weft.Mcp;

namespace Weft.Tests;

/// <summary>
/// Exercises the MCP bridge's connection pool against a real server.
/// </summary>
[TestClass]
public sealed class WeftBridgeTests
{
    /// <summary>
    /// Gets the test context supplied by the runner.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies a burst of returned connections keeps only the idle capacity open and closes the rest.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ReturnBeyondIdleCapacityClosesExtraConnections()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var bridge = new WeftBridge(token => ControlClient.ConnectAsync(fixture.SocketPath, token));
            var clients = new List<ControlClient>();
            await using (bridge.ConfigureAwait(false))
            {
                var leases = new List<ControlLease>();
                for (int i = 0; i < WeftBridge.IdleCapacity + 2; i++)
                {
                    ControlLease lease = await bridge.LeaseAsync(cancellationToken).ConfigureAwait(false);
                    leases.Add(lease);
                    clients.Add(lease.Client);
                }

                foreach (ControlLease lease in leases)
                {
                    lease.Dispose();
                }

                // The extras close asynchronously after their return.
                while (clients.Skip(WeftBridge.IdleCapacity).Any(client => !client.Closed.IsCompleted))
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                Assert.IsTrue(clients.Take(WeftBridge.IdleCapacity).All(client => !client.Closed.IsCompleted));
            }

            Assert.IsTrue(clients.All(client => client.Closed.IsCompleted));
        }
    }
}
