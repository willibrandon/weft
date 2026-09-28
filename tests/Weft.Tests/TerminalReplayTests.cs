using Hex1b;
using Hex1b.Automation;
using Weft.Client;
using Weft.Protocol;

namespace Weft.Tests;

/// <summary>
/// Verifies attached terminal views preserve the server's rendering state across replay and live output.
/// </summary>
[TestClass]
public sealed class TerminalReplayTests
{
    /// <summary>
    /// Gets the test context supplied by the runner.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies cursor save and restore sequences produce the same screen in the server and an attached view.
    /// </summary>
    /// <param name="attachBeforeSave">Whether the view observes the save itself.</param>
    /// <param name="rightEdge">Whether the saved cursor carries pending wrap at the right edge.</param>
    /// <param name="resizeBeforeAttach">Whether the server changes size after saving the cursor.</param>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task CursorRestorePreservesScreen(bool attachBeforeSave, bool rightEdge, bool resizeBeforeAttach)
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                // The same sequences tput emits for xterm: cup, sc, rc, and ed.
                string save = rightEdge ? "print '\\e[3;80HX\\e7\\e[5;1H'" : "print '\\e[3;4H\\e7'";
                string restore = rightEdge ? "print '\\e8'" : "print '\\e8\\e[J'";
                _ = await client.CreateSessionAsync(new SessionCreateParams
                {
                    Name = "cursor-replay",
                    Command = [TestPrograms.Shell, "-c", "noecho; print 'before-save'; read; " + save
                        + "; print 'temporary prompt'; read; " + restore + "; print 'ready>'; read"]
                }, cancellationToken).ConfigureAwait(false);
                BlockWaitResult waiting = await client.WaitAsync(
                    new BlockWaitParams { Target = "cursor-replay", Pattern = "before-save", TimeoutMs = 20_000 },
                    cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(WaitOutcome.Pattern, waiting.Outcome);
                if (!attachBeforeSave)
                {
                    _ = await client.SendKeysAsync(
                        new BlockSendKeysParams { Target = "cursor-replay", Keys = ["Enter"] },
                        cancellationToken).ConfigureAwait(false);
                    waiting = await client.WaitAsync(new BlockWaitParams
                    {
                        Target = "cursor-replay",
                        Pattern = "temporary prompt",
                        TimeoutMs = 20_000
                    }, cancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(WaitOutcome.Pattern, waiting.Outcome);
                }

                if (resizeBeforeAttach)
                {
                    _ = await client.AttachAsync(
                        new SessionAttachParams { Target = "cursor-replay", Width = 100, Height = 30 },
                        cancellationToken).ConfigureAwait(false);
                }

                BlockInfo block = await client.GetBlockAsync("cursor-replay", cancellationToken).ConfigureAwait(false);
                Hex1bTerminal terminal = Hex1bTerminal.CreateBuilder()
                    .WithDimensions(block.Width, block.Height)
                    .WithHmp1UdsClient(block.SocketPath, options => options.DefaultRole = Hmp1Role.Secondary)
                    .WithHeadless()
                    .Build();
                await using (terminal.ConfigureAwait(false))
                {
                    using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    Task run = terminal.RunAsync(stopping.Token);
                    try
                    {
                        var automator = new Hex1bTerminalAutomator(terminal, TimeSpan.FromSeconds(20));
                        if (attachBeforeSave)
                        {
                            await automator.WaitUntilTextAsync("before-save").ConfigureAwait(false);
                            _ = await client.SendKeysAsync(
                                new BlockSendKeysParams { Target = block.Id, Keys = ["Enter"] },
                                cancellationToken).ConfigureAwait(false);
                        }

                        await automator.WaitUntilTextAsync("temporary prompt").ConfigureAwait(false);
                        _ = await client.SendKeysAsync(
                            new BlockSendKeysParams { Target = block.Id, Keys = ["Enter"] },
                            cancellationToken).ConfigureAwait(false);
                        await automator.WaitUntilTextAsync("ready>").ConfigureAwait(false);
                        BlockCaptureResult server = await client.CaptureAsync(
                            new BlockCaptureParams { Target = block.Id },
                            cancellationToken).ConfigureAwait(false);
                        using Hex1bTerminalSnapshot view = terminal.CreateSnapshot();
                        Assert.AreEqual(server.Width, view.Width);
                        Assert.AreEqual(server.Height, view.Height);
                        Assert.AreEqual(server.CursorX, view.CursorX);
                        Assert.AreEqual(server.CursorY, view.CursorY);
                        for (int row = 0; row < server.Height; row++)
                        {
                            Assert.AreEqual(server.Lines[row], view.GetLineTrimmed(row),
                                "View differs from server on row " + row);
                        }
                    }
                    finally
                    {
                        await stopping.CancelAsync().ConfigureAwait(false);
                        try
                        {
                            await run.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                        {
                            ClientLog.Debug("Replay test terminal stopped after cancellation.");
                        }
                    }
                }
            }
        }
    }
}
