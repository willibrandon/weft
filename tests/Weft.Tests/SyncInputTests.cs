using Weft.Client;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Tests;

/// <summary>
/// Verifies synchronized input fans keys out to every block in a tab, and that output activity is published.
/// </summary>
[TestClass]
public sealed class SyncInputTests
{
    /// <summary>
    /// Gets the test context supplied by the runner.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies keys sent to one block reach its sibling while the tab is synchronized, and stop when a block is excluded.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task SynchronizedTabFansOutInput()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                await client.CreateSessionAsync(new SessionCreateParams { Name = "sync" }, cancellationToken).ConfigureAwait(false);
                BlockInfo first = await client.GetBlockAsync("sync", cancellationToken).ConfigureAwait(false);
                BlockInfo second = await client.SplitAsync(new BlockSplitParams { Target = first.Id, Orientation = SplitOrientation.LeftRight }, cancellationToken).ConfigureAwait(false);
                TabInfo tab = await client.SyncTabAsync(new TabSyncParams { Target = "sync", Enabled = true }, cancellationToken).ConfigureAwait(false);
                Assert.IsTrue(tab.Synchronized);

                await client.SendKeysAsync(new BlockSendKeysParams { Target = first.Id, Keys = ["echo fan-$((2*21))", "Enter"] }, cancellationToken).ConfigureAwait(false);
                BlockWaitResult sibling = await client.WaitAsync(new BlockWaitParams { Target = second.Id, Pattern = "^fan-42$", TimeoutMs = 20_000 }, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(WaitOutcome.Pattern, sibling.Outcome);

                await client.SyncBlockAsync(new BlockSyncParams { Target = second.Id, Excluded = true }, cancellationToken).ConfigureAwait(false);
                await client.SendKeysAsync(new BlockSendKeysParams { Target = first.Id, Keys = ["echo only-$((3*3))", "Enter"] }, cancellationToken).ConfigureAwait(false);
                BlockWaitResult own = await client.WaitAsync(new BlockWaitParams { Target = first.Id, Pattern = "^only-9$", TimeoutMs = 20_000 }, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(WaitOutcome.Pattern, own.Outcome);
                BlockWaitResult excluded = await client.WaitAsync(new BlockWaitParams { Target = second.Id, Pattern = "^only-9$", TimeoutMs = 1_000 }, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(WaitOutcome.Timeout, excluded.Outcome);
            }
        }
    }

    /// <summary>
    /// Verifies block output publishes throttled activity events to subscribers.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task OutputPublishesActivity()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient control = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (control.ConfigureAwait(false))
            {
                ControlClient subscriber = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
                await using (subscriber.ConfigureAwait(false))
                {
                    await subscriber.SubscribeAsync(null, cancellationToken).ConfigureAwait(false);
                    await control.CreateSessionAsync(new SessionCreateParams { Name = "activity" }, cancellationToken).ConfigureAwait(false);
                    BlockInfo block = await control.GetBlockAsync("activity", cancellationToken).ConfigureAwait(false);
                    await control.SendKeysAsync(new BlockSendKeysParams { Target = block.Id, Keys = ["echo hello", "Enter"] }, cancellationToken).ConfigureAwait(false);

                    while (true)
                    {
                        ProtocolMessage message = await subscriber.Events.ReadAsync(cancellationToken).ConfigureAwait(false);
                        if (string.Equals(message.Event, ProtocolEvents.BlockOutput, StringComparison.Ordinal))
                        {
                            BlockInfo producer = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.BlockEventData).Block;
                            Assert.AreEqual(block.Id, producer.Id);
                            break;
                        }
                    }
                }
            }
        }
    }
}
