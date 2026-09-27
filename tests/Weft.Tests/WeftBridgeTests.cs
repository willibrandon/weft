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

    /// <summary>
    /// Verifies a lease returned after the bridge was disposed closes its connection instead of pooling it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ReturnAfterDisposalClosesTheConnection()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var bridge = new WeftBridge(token => ControlClient.ConnectAsync(fixture.SocketPath, token));
            ControlLease lease;
            try
            {
                lease = await bridge.LeaseAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // The bridge goes away while the lease is still out.
                await bridge.DisposeAsync().ConfigureAwait(false);
            }

            ControlClient client = lease.Client;
            lease.Dispose();

            while (!client.Closed.IsCompleted)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            _ = await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => bridge.LeaseAsync(cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies a connection still being opened when the bridge is disposed is closed and the lease refused.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task DisposalDuringAcquisitionClosesTheConnection()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            // The connect delegate disposes the bridge while the connection is still being opened.
            ControlClient? produced = null;
            WeftBridge? bridge = null;
            bridge = new WeftBridge(async token =>
            {
                await bridge!.DisposeAsync().ConfigureAwait(false);
                produced = await ControlClient.ConnectAsync(fixture.SocketPath, token).ConfigureAwait(false);
                return produced;
            });

            await using (bridge.ConfigureAwait(false))
            {
                _ = await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => bridge.LeaseAsync(cancellationToken)).ConfigureAwait(false);
                Assert.IsNotNull(produced);
                while (!produced.Closed.IsCompleted)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Verifies concurrent leases on an empty pool open their connections one at a time, so a stopped server is started once.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ConcurrentLeasesOpenConnectionsOneAtATime()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            int inFlight = 0;
            int peak = 0;
            var bridge = new WeftBridge(async token =>
            {
                int now = Interlocked.Increment(ref inFlight);
                int seen;
                do
                {
                    seen = peak;
                }
                while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) != seen);

                try
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                    return await ControlClient.ConnectAsync(fixture.SocketPath, token).ConfigureAwait(false);
                }
                finally
                {
                    _ = Interlocked.Decrement(ref inFlight);
                }
            });
            await using (bridge.ConfigureAwait(false))
            {
                ControlLease[] leases = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => bridge.LeaseAsync(cancellationToken))).ConfigureAwait(false);
                foreach (ControlLease lease in leases)
                {
                    lease.Dispose();
                }
            }

            Assert.AreEqual(1, peak);
        }
    }

    /// <summary>
    /// Verifies cancellation removes a queued opener and lets the next caller connect.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task CancelledQueuedLeaseDoesNotOpenAConnection()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int opens = 0;
            var bridge = new WeftBridge(async token =>
            {
                if (Interlocked.Increment(ref opens) == 1)
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }

                return await fixture.ConnectAsync(token).ConfigureAwait(false);
            });
            await using (bridge.ConfigureAwait(false))
            {
                Task<ControlLease> first = bridge.LeaseAsync(cancellationToken);
                await entered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                using var queuedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<ControlLease> cancelled = bridge.LeaseAsync(queuedCancellation.Token);
                Task<ControlLease> next = bridge.LeaseAsync(cancellationToken);

                await queuedCancellation.CancelAsync().ConfigureAwait(false);
                _ = await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.WaitAsync(cancellationToken)).ConfigureAwait(false);
                release.SetResult();

                ControlLease[] leases = await Task.WhenAll(first, next).ConfigureAwait(false);
                Assert.AreEqual(2, opens);
                foreach (ControlLease lease in leases)
                {
                    lease.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Verifies disposal fails queued openers without connecting them.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task DisposalFailsQueuedLeaseWithoutOpeningAConnection()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int opens = 0;
            var bridge = new WeftBridge(async token =>
            {
                _ = Interlocked.Increment(ref opens);
                entered.SetResult();
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return await fixture.ConnectAsync(token).ConfigureAwait(false);
            });
            await using (bridge.ConfigureAwait(false))
            {
                Task<ControlLease> first = bridge.LeaseAsync(cancellationToken);
                await entered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                Task<ControlLease> queued = bridge.LeaseAsync(cancellationToken);

                await bridge.DisposeAsync().ConfigureAwait(false);
                _ = await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => queued.WaitAsync(cancellationToken)).ConfigureAwait(false);
                release.SetResult();
                _ = await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => first.WaitAsync(cancellationToken)).ConfigureAwait(false);
                Assert.AreEqual(1, opens);
            }
        }
    }

    /// <summary>
    /// Verifies a failed request write prevents a still-open connection from returning to the pool.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task FailedRequestWriteDiscardsTheLease()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var bridge = new WeftBridge(fixture.ConnectAsync);
            await using (bridge.ConfigureAwait(false))
            {
                ControlLease first = await bridge.LeaseAsync(cancellationToken).ConfigureAwait(false);
                ControlClient client = first.Client;
                using var cancelledWrite = new CancellationTokenSource();
                await cancelledWrite.CancelAsync().ConfigureAwait(false);

                _ = await Assert.ThrowsAsync<OperationCanceledException>(() => client.ListSessionsAsync(cancelledWrite.Token)).ConfigureAwait(false);
                Assert.IsTrue(client.TransportFailed);
                Assert.IsFalse(client.Closed.IsCompleted);

                first.Dispose();
                ControlLease next = await bridge.LeaseAsync(cancellationToken).ConfigureAwait(false);
                Assert.AreNotSame(client, next.Client);
                next.Dispose();
                await client.Closed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
