using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using Weft.Client;
using Weft.Core;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Verifies windows share sessions, release what they held, and survive losing the server.
/// </summary>
[TestClass]
public sealed class WindowLifecycleTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Two windows attach to one session, follow each other's resizing, and one survives the other closing.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task TwoWindowsShareOneSession()
    {
        return DesktopApp.RunWindowAsync(TestContext, async first =>
        {
            string pane = first.Active.Id;
            TestWindow second = await TestWindow.OpenAsync(TestContext.CancellationToken).ConfigureAwait(true);
            await using (second.ConfigureAwait(true))
            {
                _ = await second.WaitAsync(frame => frame.Blocks.Count == 1 && frame.Blocks[0].Id == pane)
                    .ConfigureAwait(true);

                // The session takes the size of the client that changed last, so the new window settles first.
                await second.UntilAsync(() => !VisualTree.Descendants(second.Content).OfType<TextBlock>()
                    .Any(text => Automation.NameOf(text) == "Connection status")).ConfigureAwait(true);
                await second.SettleAsync().ConfigureAwait(true);
                await second.DelayAsync(300).ConfigureAwait(true);
                foreach ((int width, int height) in new[] { (620, 380), (1280, 820), (780, 500) })
                {
                    int before = first.Frame.Blocks[0].Width;
                    first.Window.SetSize(width, height);
                    DesktopFrame resized = await first.WaitAsync(frame => frame.Blocks[0].Width != before)
                        .ConfigureAwait(true);
                    _ = await second.WaitAsync(frame => frame.Blocks[0].Width == first.Frame.Blocks[0].Width)
                        .ConfigureAwait(true);
                    Assert.AreEqual(pane, resized.Blocks[0].Id);
                }
            }

            await first.RunAsync(@"print 'WINDOW\055SURVIVED\n'").ConfigureAwait(true);
            _ = await first.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("WINDOW-SURVIVED", StringComparison.Ordinal)).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// A closed window releases its frames and images, and opening and closing windows reaches a stable size.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public Task ClosedWindowsReleaseWhatTheyHeld()
    {
        return DesktopApp.RunWindowAsync(TestContext, async first =>
        {
            await first.RunAsync(@"print '\033_Ga=T,f=32,s=2,v=1,c=8,r=4,q=2;/wAA/wD/AP8=\033\\'").ConfigureAwait(true);
            _ = await first.WaitAsync(frame => frame.Blocks[0].Images.Count != 0).ConfigureAwait(true);
            TerminalSurface released;
            TestWindow second = await TestWindow.OpenAsync(TestContext.CancellationToken).ConfigureAwait(true);
            await using (second.ConfigureAwait(true))
            {
                released = second.Surface;
                _ = await second.WaitAsync(frame => frame.Blocks[0].Images.Count != 0).ConfigureAwait(true);
                await second.UntilAsync(() => released.RasterCacheCount != 0).ConfigureAwait(true);
            }

            Assert.IsNull(released.Frame, "A closed window kept its frame.");
            Assert.AreEqual(0, released.RasterCacheCount, "A closed window kept decoded images.");
            Assert.AreEqual(0, released.RasterCacheBytes);

            long baseline = 0;
            for (int cycle = 0; cycle < 8; cycle++)
            {
                if (cycle == 3)
                {
                    baseline = Collect();
                }

                TestWindow churn = await TestWindow.OpenAsync(TestContext.CancellationToken).ConfigureAwait(true);
                await using (churn.ConfigureAwait(true))
                {
                    _ = await churn.WaitAsync(frame => frame.Blocks.Count == 1).ConfigureAwait(true);
                }
            }

            long growth = Collect() - baseline;
            Assert.IsLessThan(32L * 1024 * 1024, growth, "Opening and closing windows kept growing the managed heap.");
            Assert.AreEqual(1, first.Surface.RasterCacheCount, "The remaining window keeps only its own image.");
        });
    }

    /// <summary>
    /// Losing the server keeps the last content, reports the loss, drops input, and reconnects once it returns.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task LosingTheServerKeepsContentUntilItReturns()
    {
        return DesktopApp.RunWindowAsync(TestContext, async (window, server) =>
        {
            await window.RunAsync(@"print 'BEFORE\055LOSS\n'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("BEFORE-LOSS", StringComparison.Ordinal)).ConfigureAwait(true);
            await ShutdownAsync(server).ConfigureAwait(true);
            DesktopFrame lost = await window.WaitThroughErrorsAsync(frame => !frame.Connected).ConfigureAwait(true);
            Assert.Contains("BEFORE-LOSS", TestWindow.Text(lost),
                "The window cleared its content when the server went away.");
            TextBlock status = await window.FindAsync<TextBlock>("Connection status").ConfigureAwait(true);
            Assert.StartsWith("Reconnecting", status.Text);

            window.Input.Text = "lost-input";
            await window.UntilAsync(() => window.Input.Text.Length == 0).ConfigureAwait(true);

            ControlClient restarted = await ServerLauncher.ConnectOrStartAsync(server.RuntimeDirectory,
                DesktopApp.Server, TestContext.CancellationToken).ConfigureAwait(true);
            await restarted.DisposeAsync().ConfigureAwait(true);
            // Shutting the server down ended its sessions, so the window offers a new one.
            _ = await window.WaitThroughErrorsAsync(frame => frame.Connected && frame.Error is null
                && frame.Sessions.Count == 0).ConfigureAwait(true);
            await window.UntilAsync(() => !VisualTree.Descendants(window.Content).OfType<TextBlock>()
                .Any(text => Automation.NameOf(text) == "Connection status")).ConfigureAwait(true);
            await window.InvokeAsync("New Session…").ConfigureAwait(true);
            ContentDialog dialog = await window.DialogAsync("New Session").ConfigureAwait(true);
            await window.CloseDialogAsync(dialog, "PrimaryButton").ConfigureAwait(true);
            _ = await window.WaitForPromptAsync().ConfigureAwait(true);
            await window.RunAsync(@"print 'AFTER\055RECONNECT\n'").ConfigureAwait(true);
            DesktopFrame after = await window.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("AFTER-RECONNECT", StringComparison.Ordinal)).ConfigureAwait(true);
            Assert.DoesNotContain("lost-input", TestWindow.Text(after), "Input typed while disconnected was replayed.");
        });
    }

    private static long Collect()
    {
        // Forcing the measurement collects and finalizes until the heap size settles.
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static async Task ShutdownAsync(PrivateServer server)
    {
        var start = new ProcessStartInfo(DesktopApp.Server) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("shutdown");
        start.Environment[WeftPaths.SocketDirectoryVariable] = server.RuntimeDirectory;
        using Process process = Process.Start(start) ?? throw new AssertFailedException("The server did not start.");
        await process.WaitForExitAsync().ConfigureAwait(true);
    }
}
