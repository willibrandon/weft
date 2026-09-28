using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Weft.Client;
using Windows.Foundation;
using Windows.System;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Verifies pointer and accessible selection against real terminal text, including retained history.
/// </summary>
[TestClass]
public sealed class TerminalSelectionTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A click leaves the caret alone, a drag selects, and another click clears the selection.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task ClickLeavesTheCaretAndDragSelects()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            // A steady cursor keeps the pixel comparisons independent of blinking.
            await window.RunAsync(@"print '\033[2J\033[H\033[2 qSelect these words\n'").ConfigureAwait(true);
            DesktopFrame shown = await window.WaitAsync(frame =>
                TestWindow.Text(frame).StartsWith("Select these words", StringComparison.Ordinal)
                && TestWindow.Text(frame).TrimEnd().EndsWith('$')
                && frame.Blocks[0].CursorShape == 2).ConfigureAwait(true);
            DesktopBlockFrame block = shown.Blocks[0];
            Point empty = window.CellCenter(block, (6 * block.Width) + block.Width - 10);
            window.Surface.FocusTerminal();
            (int left, int top, int right, int bottom) = window.SurfacePixels();
            WindowCapture before = await window.CaptureAsync().ConfigureAwait(true);
            _ = window.Surface.PointerDown(empty, 1, VirtualKeyModifiers.None);
            WindowCapture held = await window.CaptureAsync().ConfigureAwait(true);
            Assert.IsEmpty(before.ChangedRows(held, left, top, right, bottom),
                "A plain pointer press hid the caret or painted a selection.");
            window.Surface.PointerMove(new Point(empty.X + 1, empty.Y + 1), leftButton: true, contact: true,
                VirtualKeyModifiers.None);
            window.Surface.PointerUp(new Point(empty.X + 1, empty.Y + 1), VirtualKeyModifiers.None);
            await window.DelayAsync(200).ConfigureAwait(true);
            Assert.IsNull(window.Frame.Blocks[0].Selection, "Pointer jitter left a selection.");
            Assert.IsEmpty(
                before.ChangedRows(await window.CaptureAsync().ConfigureAwait(true), left, top, right, bottom),
                "A click with pointer jitter changed the terminal's pixels.");

            Point start = window.CellCenter(block, 0);
            Point end = window.CellCenter(block, 5);
            window.Drag(start, end);
            _ = await window.WaitAsync(frame => frame.Blocks[0].Selection?.Text == "Select").ConfigureAwait(true);
            Assert.AreEqual("Select", window.Surface.SelectedText);
            ITextProvider text = TextProvider(window);
            Assert.AreEqual("Select", text.GetSelection().Single().GetText(-1),
                "The accessible selection differs from the text Copy would take.");

            window.Click(empty);
            _ = await window.WaitAsync(frame => frame.Blocks[0].Selection is null).ConfigureAwait(true);
            Assert.IsEmpty(window.Surface.SelectedText);

            // A click right after a drag clears the selection even before the drag's frame arrives.
            window.Drag(start, end);
            window.Click(empty);
            await window.RunAsync(@"print 'selection\055cleared\n'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("selection-cleared", StringComparison.Ordinal)).ConfigureAwait(true);
            Assert.IsNull(window.Frame.Blocks[0].Selection, "A quick click left the drag's selection behind.");
        });
    }

    /// <summary>
    /// Double-clicking selects a word and triple-clicking selects the row.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task RepeatedClicksSelectWordsAndRows()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print '\033[2J\033[HSelect these words\n'").ConfigureAwait(true);
            DesktopFrame shown = await window.WaitAsync(frame =>
                TestWindow.Text(frame).StartsWith("Select these words", StringComparison.Ordinal)).ConfigureAwait(true);
            Point word = window.CellCenter(shown.Blocks[0], 8);
            window.Click(word);
            window.Click(word);
            _ = await window.WaitAsync(frame => frame.Blocks[0].Selection?.Text == "these").ConfigureAwait(true);
            window.Click(word);
            _ = await window.WaitAsync(frame => frame.Blocks[0].Selection?.Text.TrimEnd() == "Select these words")
                .ConfigureAwait(true);
        });
    }

    /// <summary>
    /// A drag above the pane scrolls history while held and stops when released.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task DraggingPastTheEdgeScrollsHistoryUntilRelease()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync("repeat 1 100 'WINDOWS-HISTORY-%03d\\n'").ConfigureAwait(true);
            DesktopFrame live = await window.WaitAsync(frame => frame.Blocks[0].HistoryLines > 30
                && TestWindow.Text(frame).Contains("WINDOWS-HISTORY-100", StringComparison.Ordinal)
                && TestWindow.Text(frame).TrimEnd().EndsWith('$')).ConfigureAwait(true);
            DesktopBlockFrame block = live.Blocks[0];
            int index = TestWindow.IndexOf(block, "WINDOWS-HISTORY-100");
            Point start = window.CellCenter(block, index);
            Point right = window.CellCenter(block, index + 6);
            Point outside = new(start.X, window.Surface.CellRect(block, 0).Y - (3 * window.Surface.Font.CellHeight));

            _ = window.Surface.PointerDown(start, 1, VirtualKeyModifiers.None);
            window.Surface.PointerMove(right, leftButton: true, contact: true, VirtualKeyModifiers.None);
            DesktopFrame anchored = await window.WaitAsync(frame => frame.Blocks[0].Selection is not null)
                .ConfigureAwait(true);
            Assert.AreEqual("WINDOWS", anchored.Blocks[0].Selection!.Text, "Selection started away from the pointer.");
            window.Surface.PointerMove(outside, leftButton: true, contact: true, VirtualKeyModifiers.None);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset >= 12
                && frame.Blocks[0].Selection?.Text.Split('\n').Length > block.Height).ConfigureAwait(true);
            window.Surface.PointerUp(outside, VirtualKeyModifiers.None);
            await window.DelayAsync(200).ConfigureAwait(true);
            int stopped = window.Frame.Blocks[0].ScrollOffset;
            await window.DelayAsync(200).ConfigureAwait(true);
            Assert.AreEqual(stopped, window.Frame.Blocks[0].ScrollOffset, "Autoscroll continued after release.");
            Assert.Contains("WINDOWS-HISTORY-099", window.Surface.SelectedText, "The selection lost history rows.");

            await window.MenuAsync("View", "Resume Live Output").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].Selection is null && frame.Blocks[0].ViewVersion == 0)
                .ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Select All holds its text while output arrives, and resuming shows the new output.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task SelectionHoldsWhileOutputArrives()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print 'BEFORE\055OUTPUT\n'; sleep 1.5; print 'NEW\055OUTPUT\n'")
                .ConfigureAwait(true);
            _ = await window.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("BEFORE-OUTPUT", StringComparison.Ordinal)).ConfigureAwait(true);
            await window.MenuAsync("Edit", "Select All").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].Selection is not null).ConfigureAwait(true);
            string selected = window.Surface.SelectedText;
            Assert.Contains("BEFORE-OUTPUT", selected);
            await window.DelayAsync(2500).ConfigureAwait(true);
            Assert.AreEqual(selected, window.Surface.SelectedText, "Output changed the selected text.");
            Assert.DoesNotContain("NEW-OUTPUT", TestWindow.Text(window.Frame), "Output replaced the selected view.");
            await window.MenuAsync("View", "Resume Live Output").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == 0 && frame.Blocks[0].Selection is null
                && TestWindow.Text(frame).Contains("NEW-OUTPUT", StringComparison.Ordinal)).ConfigureAwait(true);
        });
    }

    private static ITextProvider TextProvider(TestWindow window)
    {
        AutomationPeer peer = FrameworkElementAutomationPeer.CreatePeerForElement(window.Input);
        return peer.GetPattern(PatternInterface.Text) as ITextProvider
            ?? throw new AssertFailedException("The terminal has no text pattern.");
    }
}
