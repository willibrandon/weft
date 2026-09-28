using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Weft.Client;
using Windows.Foundation;
using Windows.System;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Scrolls retained history with the native scroll bar, the mouse wheel, and precise scrolling.
/// </summary>
[TestClass]
public sealed class TerminalHistoryTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The scroll bar mirrors history and moves it through UI Automation, and Resume returns to live output.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task ScrollBarMovesThroughHistory()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            DesktopBlockFrame live = await FillHistoryAsync(window).ConfigureAwait(true);
            ScrollBar bar = VisualTree.Descendants(window.Surface).OfType<ScrollBar>()
                .Single(item => AutomationProperties.GetName(item) == "Terminal history");
            await window.UntilAsync(() => bar.Visibility == Visibility.Visible && bar.Maximum == live.HistoryLines)
                .ConfigureAwait(true);
            Assert.AreEqual(live.Height, bar.ViewportSize, "The thumb represents the visible rows.");
            Assert.AreEqual(bar.Maximum, bar.Value, "The thumb starts at live output.");

            Automation.SetRange(bar, 0);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == frame.Blocks[0].HistoryLines
                && TestWindow.Text(frame).Contains("WINDOWS-HISTORY-001", StringComparison.Ordinal))
                .ConfigureAwait(true);
            Automation.SetRange(bar, bar.Maximum / 2);
            DesktopFrame middle = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset is > 0
                && frame.Blocks[0].ScrollOffset < frame.Blocks[0].HistoryLines).ConfigureAwait(true);
            Assert.AreEqual(0.5, (double)middle.Blocks[0].ScrollOffset / live.HistoryLines, 0.15,
                "The middle of the track is the middle of history.");

            await window.MenuAsync("View", "Resume Live Output").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == 0).ConfigureAwait(true);
            await window.UntilAsync(() => bar.Value == bar.Maximum).ConfigureAwait(true);
            Automation.SetRange(bar, bar.Maximum - bar.LargeChange);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == (int)bar.LargeChange)
                .ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Wheel detents scroll three rows, precise deltas accumulate, and fast scrolling stops at the ends.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task WheelScrollsByRows()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            DesktopBlockFrame live = await FillHistoryAsync(window).ConfigureAwait(true);
            Point center = window.CellCenter(live, (live.Height / 2 * live.Width) + (live.Width / 2));
            Assert.IsTrue(window.Surface.Wheel(center, 120, VirtualKeyModifiers.None));
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == 3).ConfigureAwait(true);
            await ResumeAsync(window).ConfigureAwait(true);

            for (int step = 0; step < 4; step++)
            {
                _ = window.Surface.Wheel(center, 20, VirtualKeyModifiers.None);
            }

            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == 2).ConfigureAwait(true);
            await ResumeAsync(window).ConfigureAwait(true);

            _ = window.Surface.Wheel(center, 120 * 1000, VirtualKeyModifiers.None);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == frame.Blocks[0].HistoryLines)
                .ConfigureAwait(true);
            _ = window.Surface.Wheel(center, -40, VirtualKeyModifiers.None);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == frame.Blocks[0].HistoryLines - 1)
                .ConfigureAwait(true);
            await ResumeAsync(window).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Typing while scrolled back returns to live output before the text reaches the shell.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task TypingReturnsToLiveOutput()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            DesktopBlockFrame live = await FillHistoryAsync(window).ConfigureAwait(true);
            _ = window.Surface.Wheel(window.CellCenter(live, 0), 360, VirtualKeyModifiers.None);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == 9).ConfigureAwait(true);
            await window.RunAsync(@"print 'BACK\055LIVE\n'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == 0
                && TestWindow.Text(frame).Contains("BACK-LIVE", StringComparison.Ordinal)).ConfigureAwait(true);
        });
    }

    private static async Task<DesktopBlockFrame> FillHistoryAsync(TestWindow window)
    {
        await window.RunAsync("repeat 1 100 'WINDOWS-HISTORY-%03d\\n'").ConfigureAwait(true);
        DesktopFrame frame = await window.WaitAsync(frame => frame.Blocks[0].HistoryLines > 30
            && TestWindow.Text(frame).Contains("WINDOWS-HISTORY-100", StringComparison.Ordinal)
            && TestWindow.Text(frame).TrimEnd().EndsWith('$')).ConfigureAwait(true);
        return frame.Blocks[0];
    }

    private static async Task ResumeAsync(TestWindow window)
    {
        window.Surface.Resume();
        _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == 0).ConfigureAwait(true);
    }
}
