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
    /// Verifies a burst of connections returned at the same time keeps only the idle capacity open and closes the rest.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ConcurrentReturnsBeyondIdleCapacityCloseExtraConnections()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var clients = new List<ControlClient>();
            var bridge = new WeftBridge(token => ControlClient.ConnectAsync(fixture.SocketPath, token));
            await using (bridge.ConfigureAwait(false))
            {
                var leases = new List<ControlLease>();
                for (int i = 0; i < WeftBridge.IdleCapacity + 4; i++)
                {
                    ControlLease lease = await bridge.LeaseAsync(cancellationToken).ConfigureAwait(false);
                    leases.Add(lease);
                    clients.Add(lease.Client);
                }

                // Every lease returns on its own thread at once, so the capacity check races with itself.
                using var release = new Barrier(leases.Count);
                await Task.WhenAll(leases.Select(lease => Task.Run(() =>
                {
                    release.SignalAndWait(cancellationToken);
                    lease.Dispose();
                }, cancellationToken))).ConfigureAwait(false);

                // The extras close asynchronously after their return; the pool closes exactly the surplus.
                int surplus = clients.Count - WeftBridge.IdleCapacity;
                while (clients.Count(client => client.Closed.IsCompleted) < surplus)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                Assert.AreEqual(surplus, clients.Count(client => client.Closed.IsCompleted));
            }

            Assert.IsTrue(clients.All(client => client.Closed.IsCompleted));
        }
    }
}
