using Weft.Client;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Tests;

/// <summary>
/// Drives a real server over its control socket with real shells.
/// </summary>
[TestClass]
public sealed class ServerTests
{
    /// <summary>
    /// Gets the test context supplied by the runner.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies a fresh server answers server.info and reports its protocol version.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ServerInfoReportsProtocol()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                ServerInfoResult info = await client.ServerInfoAsync(cancellationToken).ConfigureAwait(false);

                Assert.AreEqual(ProtocolVersion.Current, client.Hello.Protocol);
                Assert.AreEqual(ProtocolVersion.Current, info.Protocol);
                Assert.AreEqual(0, info.Sessions);
            }
        }
    }

    /// <summary>
    /// Verifies a session starts a real shell, accepts typed input, and captures the shell's output.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task SessionRunsShellAndCapturesOutput()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                SessionInfo session = await client.CreateSessionAsync(new SessionCreateParams { Name = "work" }, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual("work", session.Name);
                Assert.AreEqual(1, session.Blocks);

                BlockInfo block = await client.GetBlockAsync("work", cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(BlockState.Running, block.State);
                Assert.IsNotNull(block.Pid);
                await ServerFixture.WaitForPromptAsync(client, block.Id, cancellationToken).ConfigureAwait(false);

                await client.SendKeysAsync(new BlockSendKeysParams { Target = block.Id, Keys = ["echo weft-$((6*7))", "Enter"] }, cancellationToken).ConfigureAwait(false);
                BlockWaitResult wait = await client.WaitAsync(new BlockWaitParams { Target = block.Id, Pattern = "^weft-42$", TimeoutMs = 20_000 }, cancellationToken).ConfigureAwait(false);

                Assert.AreEqual(WaitOutcome.Pattern, wait.Outcome);
                Assert.AreEqual("weft-42", wait.Match);
                BlockCaptureResult capture = await client.CaptureAsync(new BlockCaptureParams { Target = block.Id }, cancellationToken).ConfigureAwait(false);
                Assert.Contains("weft-42", capture.Lines);
                Assert.IsGreaterThanOrEqualTo(wait.Revision, capture.Revision);
            }
        }
    }

    /// <summary>
    /// Verifies splitting creates a second block with its own process and geometry that fills the session.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task SplitCreatesSecondBlockWithGeometry()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                await client.CreateSessionAsync(new SessionCreateParams { Name = "split" }, cancellationToken).ConfigureAwait(false);
                BlockInfo first = await client.GetBlockAsync("split", cancellationToken).ConfigureAwait(false);

                BlockInfo second = await client.SplitAsync(new BlockSplitParams { Target = first.Id, Orientation = SplitOrientation.LeftRight }, cancellationToken).ConfigureAwait(false);

                Assert.AreNotEqual(first.Pid, second.Pid);
                Assert.IsTrue(second.Active);
                LayoutInfo layout = await client.GetLayoutAsync("split", cancellationToken).ConfigureAwait(false);
                Assert.HasCount(2, layout.Tiled);
                Assert.AreEqual(80, layout.Tiled[0].Width + layout.Tiled[1].Width);
                BlockInfo refreshed = await client.GetBlockAsync(first.Id, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(layout.Tiled[0].Width - 2, refreshed.Width);
                Assert.IsTrue(LayoutSerializer.TryParse(layout.Serialized, out _));
            }
        }
    }

    /// <summary>
    /// Verifies block.run awaits a command's exit and returns its code and output.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task RunReturnsExitCodeAndOutput()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                await client.CreateSessionAsync(new SessionCreateParams { Name = "run" }, cancellationToken).ConfigureAwait(false);

                BlockRunResult result = await client.RunAsync(new BlockRunParams { Target = "run", Command = ["/bin/sh", "-c", "echo hello-run; exit 3"], TimeoutMs = 20_000 }, cancellationToken).ConfigureAwait(false);

                Assert.IsTrue(result.Completed);
                Assert.AreEqual(3, result.ExitCode);
                Assert.Contains("hello-run", result.Output);
                Assert.IsFalse(result.Truncated);
                BlockListResult blocks = await client.ListBlocksAsync("run", cancellationToken).ConfigureAwait(false);
                Assert.HasCount(1, blocks.Blocks);
            }
        }
    }

    /// <summary>
    /// Verifies events stream to a subscriber and closing a session publishes its closure.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task EventsStreamToSubscribers()
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

                    await control.CreateSessionAsync(new SessionCreateParams { Name = "events" }, cancellationToken).ConfigureAwait(false);
                    await control.CloseSessionAsync("events", cancellationToken).ConfigureAwait(false);

                    List<string> names = [];
                    while (!names.Contains(ProtocolEvents.SessionClosed, StringComparer.Ordinal))
                    {
                        ProtocolMessage message = await subscriber.Events.ReadAsync(cancellationToken).ConfigureAwait(false);
                        names.Add(message.Event!);
                    }

                    Assert.Contains(ProtocolEvents.SessionCreated, names);
                    Assert.Contains(ProtocolEvents.BlockCreated, names);
                    Assert.Contains(ProtocolEvents.SessionClosed, names);
                }
            }
        }
    }

    /// <summary>
    /// Verifies a client's viewport drives the authoritative size under the latest policy.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AttachAppliesLatestClientSize()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                SessionAttachResult attached = await client.AttachAsync(new SessionAttachParams { Target = "sized", Width = 100, Height = 30, Name = "test" }, cancellationToken).ConfigureAwait(false);

                Assert.AreEqual("sized", attached.Session.Name);
                Assert.AreEqual(100, attached.Session.Width);
                Assert.AreEqual(30, attached.Layout.Height);
                Assert.HasCount(1, attached.Blocks);
                Assert.AreEqual(98, attached.Blocks[0].Width);
                await client.SetSizeAsync(new SessionSetSizeParams { Client = attached.Client.Id, Width = 60, Height = 20 }, cancellationToken).ConfigureAwait(false);
                SessionInfo resized = await client.GetSessionAsync("sized", cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(60, resized.Width);
            }
        }
    }

    /// <summary>
    /// Verifies a stored session is resurrected with its layout after the server restarts.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task SessionResurrectsAfterRestart()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var first = ServerFixture.Start();
        string root;
        string layoutBefore;
        await using (first.ConfigureAwait(false))
        {
            root = first.Root;
            await first.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await first.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                await client.CreateSessionAsync(new SessionCreateParams { Name = "durable" }, cancellationToken).ConfigureAwait(false);
                BlockInfo block = await client.GetBlockAsync("durable", cancellationToken).ConfigureAwait(false);
                await client.SplitAsync(new BlockSplitParams { Target = block.Id, Orientation = SplitOrientation.TopBottom }, cancellationToken).ConfigureAwait(false);
                LayoutInfo layout = await client.GetLayoutAsync("durable", cancellationToken).ConfigureAwait(false);
                layoutBefore = layout.Serialized;
            }

            await first.StopKeepingStateAsync().ConfigureAwait(false);
        }

        var second = ServerFixture.Resume(root);
        await using (second.ConfigureAwait(false))
        {
            await second.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient resumed = await second.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (resumed.ConfigureAwait(false))
            {
                SessionAttachResult attached = await resumed.AttachAsync(new SessionAttachParams { Target = "durable", Width = 80, Height = 24 }, cancellationToken).ConfigureAwait(false);

                Assert.HasCount(2, attached.Blocks);
                Assert.HasCount(2, attached.Layout.Tiled);
                Assert.AreEqual(24, attached.Layout.Tiled[0].Height + attached.Layout.Tiled[1].Height);
                Assert.IsTrue(LayoutSerializer.TryParse(layoutBefore, out _));
            }
        }
    }
}
