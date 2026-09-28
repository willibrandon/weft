using Hex1b;
using Hex1b.Automation;
using Hex1b.Input;
using System.Diagnostics;
using System.Globalization;
using Weft.Client;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Tests;

/// <summary>
/// Drives the attach UI headlessly against a real server: rendering, direct shortcuts, help, and exit.
/// </summary>
[TestClass]
public sealed class AttachAppTests
{
    /// <summary>
    /// Gets the test context supplied by the runner.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies direct shortcuts split a block and exit the view while leaving the session alive.
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

            await automator.KeyAsync(Hex1bKey.F3, cancellationToken).ConfigureAwait(false);
            await WaitForFramesAsync(app, automator, 2).ConfigureAwait(false);

            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                BlockListResult blocks = await client.ListBlocksAsync("ui", cancellationToken).ConfigureAwait(false);
                Assert.HasCount(2, blocks.Blocks);
            }

            await automator.TypeAsync("echo ui-ok-$((3*4))", cancellationToken).ConfigureAwait(false);
            await automator.EnterAsync(cancellationToken).ConfigureAwait(false);
            await automator.WaitUntilTextAsync("ui-ok-12").ConfigureAwait(false);

            await automator.KeyAsync(Hex1bKey.F10, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Verifies a configured direct shortcut drives the same action.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task ConfiguredShortcutsApply()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var config = new WeftConfig { Bindings = { ["f11"] = "split.down" } };
            var app = new AttachApp(new AttachOptions { SocketPath = fixture.SocketPath, Target = "keys", Name = "test", Headless = (100, 30), Config = config });
            Task run = app.RunAsync(cancellationToken);
            Hex1bTerminal terminal = await WaitForTerminalAsync(app, run, cancellationToken).ConfigureAwait(false);
            var automator = new Hex1bTerminalAutomator(terminal, TimeSpan.FromSeconds(20));
            await automator.WaitUntilTextAsync(" keys ").ConfigureAwait(false);

            await automator.KeyAsync(Hex1bKey.F11, cancellationToken).ConfigureAwait(false);
            await WaitForFramesAsync(app, automator, 2).ConfigureAwait(false);

            await automator.KeyAsync(Hex1bKey.F10, cancellationToken).ConfigureAwait(false);
            while (!run.IsCompleted)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }

            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                LayoutInfo layout = await client.GetLayoutAsync("keys", cancellationToken).ConfigureAwait(false);
                Assert.HasCount(2, layout.Tiled);
                Assert.AreEqual(layout.Tiled[0].X, layout.Tiled[1].X);
            }
        }
    }

    /// <summary>
    /// Verifies help opens through terminal keys and mouse clicks, restores terminal focus, and respects read-only input and disabled shortcuts.
    /// </summary>
    /// <param name="readOnly">Whether the attached client can type into the shell.</param>
    /// <param name="disableShortcuts">Whether help is accessible only through its button.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [DataRow(false, false, 100, 30)]
    [DataRow(true, false, 100, 30)]
    [DataRow(false, true, 100, 30)]
    [DataRow(false, false, 188, 51)]
    [DataRow(false, false, 60, 12)]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task HelpOpensAndReturnsToTerminal(bool readOnly, bool disableShortcuts, int width, int height)
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var config = new WeftConfig();
            if (disableShortcuts)
            {
                config.Bindings["f1"] = "none";
            }

            var app = new AttachApp(new AttachOptions { SocketPath = fixture.SocketPath, Target = "help", Name = "test", Headless = (width, height), ReadOnly = readOnly, Config = config });
            Task run = app.RunAsync(cancellationToken);
            Hex1bTerminal terminal = await WaitForTerminalAsync(app, run, cancellationToken).ConfigureAwait(false);
            var automator = new Hex1bTerminalAutomator(terminal, TimeSpan.FromSeconds(20));
            await automator.WaitUntilTextAsync(disableShortcuts ? "Help" : "F1 Help").ConfigureAwait(false);

            if (!disableShortcuts)
            {
                await automator.KeyAsync(Hex1bKey.F1, cancellationToken).ConfigureAwait(false);
                await automator.WaitUntilTextAsync("Search commands").ConfigureAwait(false);
                await automator.KeyAsync(Hex1bKey.F1, cancellationToken).ConfigureAwait(false);
                await automator.WaitUntilAsync(snapshot => !snapshot.ContainsText("Search commands")).ConfigureAwait(false);
                await automator.KeyAsync(Hex1bKey.F1, cancellationToken).ConfigureAwait(false);
                await automator.WaitUntilTextAsync("Search commands").ConfigureAwait(false);
                if (readOnly)
                {
                    Assert.IsFalse(automator.CreateSnapshot().ContainsText("Split right"));
                }

                await automator.TypeAsync("Copy", cancellationToken).ConfigureAwait(false);
                await automator.WaitUntilAsync(snapshot => snapshot.FindText("Copy").Count >= 2).ConfigureAwait(false);
                await automator.KeyAsync(Hex1bKey.Escape, cancellationToken).ConfigureAwait(false);
                await automator.WaitUntilAsync(snapshot => !snapshot.ContainsText("Search commands")).ConfigureAwait(false);
                Assert.IsFalse(run.IsCompleted, "Closing Help must not exit weft.");
            }

            (int helpLine, int helpColumn) = automator.CreateSnapshot().FindText("Help").Single();
            await automator.ClickAtAsync(helpColumn, helpLine, ct: cancellationToken).ConfigureAwait(false);
            await automator.WaitUntilTextAsync("Search commands").ConfigureAwait(false);
            await automator.ClickAtAsync(helpColumn, helpLine, ct: cancellationToken).ConfigureAwait(false);
            await automator.WaitUntilAsync(snapshot => !snapshot.ContainsText("Search commands")).ConfigureAwait(false);
            await automator.ClickAtAsync(helpColumn, helpLine, ct: cancellationToken).ConfigureAwait(false);
            await automator.WaitUntilTextAsync("Search commands").ConfigureAwait(false);
            Assert.HasCount(1, automator.CreateSnapshot().FindText("Help and commands"));
            (int closeLine, int closeColumn) = automator.CreateSnapshot().FindText("[ Close ]").Single();
            Assert.IsLessThan(height - 1, closeLine, "Close must stay above the status bar.");
            await automator.ClickAtAsync(closeColumn, closeLine, ct: cancellationToken).ConfigureAwait(false);
            await automator.WaitUntilAsync(snapshot => !snapshot.ContainsText("Search commands")).ConfigureAwait(false);

            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                await ServerFixture.WaitForPromptAsync(client, "help", cancellationToken).ConfigureAwait(false);
                await automator.TypeAsync("echo help-ok-$((6*7))", cancellationToken).ConfigureAwait(false);
                await automator.EnterAsync(cancellationToken).ConfigureAwait(false);
                if (readOnly)
                {
                    await automator.KeyAsync(Hex1bKey.F1, cancellationToken).ConfigureAwait(false);
                    await automator.WaitUntilTextAsync("Search commands").ConfigureAwait(false);
                    BlockCaptureResult capture = await client.CaptureAsync(new BlockCaptureParams { Target = "help" }, cancellationToken).ConfigureAwait(false);
                    Assert.DoesNotContain(line => line.Contains("help-ok", StringComparison.Ordinal), capture.Lines);
                    await automator.KeyAsync(Hex1bKey.Escape, cancellationToken).ConfigureAwait(false);
                    await automator.WaitUntilAsync(snapshot => !snapshot.ContainsText("Search commands")).ConfigureAwait(false);
                }
                else
                {
                    await automator.WaitUntilTextAsync("help-ok-42").ConfigureAwait(false);
                }
            }

            (int exitLine, int exitColumn) = automator.CreateSnapshot().FindText("Exit weft").Single();
            await automator.ClickAtAsync(exitColumn, exitLine, ct: cancellationToken).ConfigureAwait(false);
            await run.WaitAsync(cancellationToken).ConfigureAwait(false);
            Assert.IsNull(app.ExitMessage);
            ControlClient after = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (after.ConfigureAwait(false))
            {
                SessionInfo session = await after.GetSessionAsync("help", cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(0, session.Clients);
                Assert.AreEqual(1, session.Blocks);
            }
        }
    }

    /// <summary>
    /// Verifies Esc and Ctrl+B reach the real process, and exiting and reattaching preserve that process.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task TerminalKeysAndExitPreserveSession()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = await fixture.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                _ = await client.CreateSessionAsync(new SessionCreateParams
                {
                    Name = "terminal-keys",
                    Command = [TestPrograms.Shell, "-c", "raw; print 'key-ready\\r\\n'; bytes 1; print 'esc-received\\r\\n'; bytes 1; cooked; print 'key-done\\n'; read"]
                }, cancellationToken).ConfigureAwait(false);
                var app = new AttachApp(new AttachOptions { SocketPath = fixture.SocketPath, Target = "terminal-keys", Headless = (100, 30) });
                Task run = app.RunAsync(cancellationToken);
                Hex1bTerminal terminal = await WaitForTerminalAsync(app, run, cancellationToken).ConfigureAwait(false);
                var automator = new Hex1bTerminalAutomator(terminal, TimeSpan.FromSeconds(20));
                await automator.WaitUntilTextAsync("key-ready").ConfigureAwait(false);
                await automator.KeyAsync(Hex1bKey.Escape, cancellationToken).ConfigureAwait(false);
                await automator.WaitUntilTextAsync("esc-received").ConfigureAwait(false);
                await automator.Ctrl().KeyAsync(Hex1bKey.B, cancellationToken).ConfigureAwait(false);
                await automator.WaitUntilTextAsync("key-done").ConfigureAwait(false);
                Assert.IsFalse(run.IsCompleted);
                BlockCaptureResult capture = await client.CaptureAsync(new BlockCaptureParams { Target = "terminal-keys" }, cancellationToken).ConfigureAwait(false);
                Assert.MatchesRegex("\\b27\\s+esc-received\\s+2\\b", string.Join('\n', capture.Lines));
                BlockInfo before = await client.GetBlockAsync("terminal-keys", cancellationToken).ConfigureAwait(false);
                (int exitLine, int exitColumn) = automator.CreateSnapshot().FindText("Exit weft").Single();
                await automator.ClickAtAsync(exitColumn, exitLine, ct: cancellationToken).ConfigureAwait(false);
                await run.WaitAsync(cancellationToken).ConfigureAwait(false);

                var reattached = new AttachApp(new AttachOptions { SocketPath = fixture.SocketPath, Target = "terminal-keys", Headless = (100, 30) });
                Task reattachedRun = reattached.RunAsync(cancellationToken);
                Hex1bTerminal reattachedTerminal = await WaitForTerminalAsync(reattached, reattachedRun, cancellationToken).ConfigureAwait(false);
                var reattachedAutomator = new Hex1bTerminalAutomator(reattachedTerminal, TimeSpan.FromSeconds(20));
                await reattachedAutomator.WaitUntilTextAsync("key-done").ConfigureAwait(false);
                BlockInfo after = await client.GetBlockAsync("terminal-keys", cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(before.Pid, after.Pid);
                Assert.AreEqual(BlockState.Running, after.State);
                await reattachedAutomator.KeyAsync(Hex1bKey.F1, cancellationToken).ConfigureAwait(false);
                await reattachedAutomator.WaitUntilTextAsync("Search commands").ConfigureAwait(false);
                await reattachedAutomator.KeyAsync(Hex1bKey.F10, cancellationToken).ConfigureAwait(false);
                await reattachedRun.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task WaitForFramesAsync(AttachApp app, Hex1bTerminalAutomator automator, int frames)
    {
        try
        {
            await automator.WaitUntilAsync(snapshot => snapshot.GetScreenText().Count(c => c == '┌') >= frames, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        }
        catch (Hex1bAutomationException exception)
        {
            string focused = app.App?.FocusedNode?.GetType().Name ?? "none";
            string route = InputRouter.LastRouteDebug ?? "none";
            string log = string.Join(" | ", ClientLog.Snapshot().TakeLast(40));
            throw new InvalidOperationException("Split did not render. focused=" + focused + " route=" + route + " status=" + app.Status + " " + app.DebugState() + " " + await ThreadPoolStateAsync().ConfigureAwait(false) + " log=" + log, exception);
        }
    }

    private static async Task<string> ThreadPoolStateAsync()
    {
        ThreadPool.GetAvailableThreads(out int workers, out int io);
        ThreadPool.GetMinThreads(out int minWorkers, out _);
        long started = Stopwatch.GetTimestamp();
        await Task.Yield();
        double scheduleMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return "threadPool(threads=" + ThreadPool.ThreadCount + " pending=" + ThreadPool.PendingWorkItemCount + " availableWorkers=" + workers + " availableIo=" + io + " minWorkers=" + minWorkers + " yieldMs=" + scheduleMs.ToString("F1", CultureInfo.InvariantCulture) + ")";
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
