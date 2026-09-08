using Hex1b;
using Hex1b.Automation;
using Hex1b.Input;
using Weft.Client;
using Weft.Protocol;

namespace Weft.Tests;

/// <summary>
/// Drives the attach UI headlessly against a real server: rendering, leader chords, and detach.
/// </summary>
[TestClass]
public sealed class AttachAppTests
{
    /// <summary>
    /// Gets the test context supplied by the runner.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies the attach UI renders the block frame and info bar, splits on the leader chord,
    /// shows shell output typed through the focused block, and detaches on the leader chord.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task AttachRendersSplitsAndDetaches()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var app = new AttachApp(new AttachOptions { SocketPath = fixture.SocketPath, Target = "ui", Name = "test", Headless = (100, 30) });
            Task run = app.RunAsync(cancellationToken);
            Hex1bTerminal terminal = await WaitForTerminalAsync(app, run, cancellationToken).ConfigureAwait(false);
            var automator = new Hex1bTerminalAutomator(terminal, TimeSpan.FromSeconds(20));

            await automator.WaitUntilTextAsync(" ui ").ConfigureAwait(false);
            await automator.WaitUntilTextAsync("100×29").ConfigureAwait(false);

            await automator.Ctrl().KeyAsync(Hex1bKey.B, cancellationToken).ConfigureAwait(false);
            await automator.KeyAsync(Hex1bKey.V, cancellationToken).ConfigureAwait(false);
            await automator.WaitUntilTextAsync("┐┌").ConfigureAwait(false);

            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                BlockListResult blocks = await client.ListBlocksAsync("ui", cancellationToken).ConfigureAwait(false);
                Assert.HasCount(2, blocks.Blocks);
            }

            await automator.TypeAsync("echo ui-ok-$((3*4))", cancellationToken).ConfigureAwait(false);
            await automator.EnterAsync(cancellationToken).ConfigureAwait(false);
            await automator.WaitUntilTextAsync("ui-ok-12").ConfigureAwait(false);

            await automator.Ctrl().KeyAsync(Hex1bKey.B, cancellationToken).ConfigureAwait(false);
            await automator.KeyAsync(Hex1bKey.D, cancellationToken).ConfigureAwait(false);
            while (!run.IsCompleted)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }

            Assert.IsNull(app.ExitMessage);
            ControlClient after = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (after.ConfigureAwait(false))
            {
                SessionInfo session = await after.GetSessionAsync("ui", cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(0, session.Clients);
                Assert.AreEqual(2, session.Blocks);
            }
        }
    }

    private static async Task<Hex1bTerminal> WaitForTerminalAsync(AttachApp app, Task run, CancellationToken cancellationToken)
    {
        while (app.Terminal is null)
        {
            if (run.IsCompleted)
            {
                throw new InvalidOperationException("The attach app stopped before creating its terminal: " + app.ExitMessage);
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }

        return app.Terminal;
    }
}
