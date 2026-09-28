using Microsoft.UI.Xaml.Controls;
using Weft.Client;
using Windows.Foundation;
using Windows.System;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Drives tabs, panes, sessions, search, and dialogs through the window's own controls.
/// </summary>
[TestClass]
public sealed class WorkspaceTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Splits a pane, adds a tab, and selects the first tab again with its panes intact.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task SplitsAndTabsKeepTheirPanes()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            string original = window.Active.Id;
            await window.MenuAsync("Pane", "Split Right").ConfigureAwait(true);
            DesktopFrame split = await window.WaitAsync(frame => frame.Blocks.Count == 2).ConfigureAwait(true);
            Assert.IsGreaterThan(split.Blocks[0].X, split.Blocks[1].X, "Split Right places the pane beside the first.");

            await AddTabAsync(window).ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Tabs.Count == 2 && frame.Blocks.Count == 1
                && frame.Blocks[0].Id != original).ConfigureAwait(true);
            await window.UntilAsync(() => window.TabItems().Length == 2).ConfigureAwait(true);

            Automation.Select(window.TabItems()[0]);
            _ = await window.WaitAsync(frame => frame.Blocks.Count == 2
                && frame.Blocks.Any(block => block.Id == original)).ConfigureAwait(true);
            Assert.AreEqual(0, window.Tabs.SelectedIndex);
        });
    }

    /// <summary>
    /// Closing an inactive tab asks first, keeps the active tab selected, and cancelling keeps the tab.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task ClosingAnInactiveTabConfirmsWithoutSelectingIt()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await AddTabAsync(window).ConfigureAwait(true);
            DesktopFrame two = await window.WaitAsync(frame => frame.Tabs.Count == 2).ConfigureAwait(true);
            string active = two.ActiveTab!;
            int selected = two.Tabs.ToList().FindIndex(tab => tab.Id == active);
            // The tab strip follows the frame on its next render.
            await window.UntilAsync(() => window.TabItems().Length == 2 && window.Tabs.SelectedIndex == selected)
                .ConfigureAwait(true);
            TabViewItem inactive = window.TabItems().Single(item => !item.IsSelected);

            Automation.Invoke(CloseButton(inactive));
            ContentDialog cancel = await window.DialogAsync("Close Tab?").ConfigureAwait(true);
            Assert.AreEqual(active, window.Frame.ActiveTab, "Closing an inactive tab selected it first.");
            await window.CloseDialogAsync(cancel, "CloseButton").ConfigureAwait(true);
            await window.DelayAsync(200).ConfigureAwait(true);
            Assert.HasCount(2, window.Frame.Tabs, "Cancel closed the tab.");

            Automation.Invoke(CloseButton(window.TabItems().Single(item => !item.IsSelected)));
            ContentDialog confirm = await window.DialogAsync("Close Tab?").ConfigureAwait(true);
            await window.CloseDialogAsync(confirm, "PrimaryButton").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Tabs.Count == 1 && frame.ActiveTab == active)
                .ConfigureAwait(true);
        });
    }

    /// <summary>
    /// The pane context menu splits the pane it was opened on.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task ContextMenuSplitsThePane()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            DesktopBlockFrame block = window.Active;
            Assert.IsFalse(
                window.Surface.PointerDown(window.CellCenter(block, block.Width + 4), 3, VirtualKeyModifiers.None),
                "A right click only opens the menu.");
            await window.RunMenuAsync("Split Below").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks.Count == 2 && frame.Blocks[1].Y > frame.Blocks[0].Y)
                .ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Dragging the divider between panes resizes them by whole cells.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task DraggingTheDividerResizesPanes()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.MenuAsync("Pane", "Split Right").ConfigureAwait(true);
            DesktopFrame split = await window.WaitAsync(frame => frame.Blocks.Count == 2).ConfigureAwait(true);
            DesktopBlockFrame left = split.Blocks.MinBy(block => block.X)!;
            DesktopBlockFrame right = split.Blocks.MaxBy(block => block.X)!;
            int row = left.Height / 2;
            Rect leftEdge = window.Surface.CellRect(left, (row * left.Width) + left.Width - 1);
            Rect rightEdge = window.Surface.CellRect(right, row * right.Width);
            var divider = new Point((leftEdge.Right + rightEdge.X) / 2, leftEdge.Y + (leftEdge.Height / 2));
            var moved = new Point(divider.X + (5 * window.Surface.Font.CellWidth) + 1, divider.Y);
            Assert.IsTrue(window.Surface.PointerDown(divider, 1, VirtualKeyModifiers.None));
            window.Surface.PointerMove(moved, leftButton: true, contact: true, VirtualKeyModifiers.None);
            window.Surface.PointerUp(moved, VirtualKeyModifiers.None);
            _ = await window.WaitAsync(frame =>
                frame.Blocks.Single(block => block.Id == left.Id).Width == left.Width + 5).ConfigureAwait(true);
            Assert.IsNull(window.Frame.Blocks.Single(block => block.Id == left.Id).Selection,
                "Resizing selected text.");
        });
    }

    /// <summary>
    /// Find searches retained history, reports its matches, keeps its query through a resize, and closes.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task FindSearchesHistory()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync("repeat 1 100 'WINDOWS-HISTORY-%03d\\n'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].HistoryLines > 30
                && TestWindow.Text(frame).TrimEnd().EndsWith('$')).ConfigureAwait(true);
            await window.MenuAsync("Edit", "Find in Terminal…").ConfigureAwait(true);
            TextBox field = await window.FindAsync<TextBox>("Find in terminal").ConfigureAwait(true);
            TestWindow.Enter(field, "WINDOWS-HISTORY-010");
            _ = await window.WaitAsync(frame => frame.Blocks[0].SearchMatches == 1
                && TestWindow.Text(frame).Contains("WINDOWS-HISTORY-010", StringComparison.Ordinal))
                .ConfigureAwait(true);
            _ = await window.FindAsync<TextBlock>(text => text.Text == "1 matching lines").ConfigureAwait(true);
            Assert.AreSame(window.Surface, VisualTree.Descendants(window.Content).OfType<TerminalSurface>().Single(),
                "Opening Find replaced the terminal surface.");

            window.Window.SetSize(900, 600);
            _ = await window.WaitAsync(frame => frame.Blocks[0].SearchQuery == "WINDOWS-HISTORY-010"
                && frame.Blocks[0].SearchMatches == 1).ConfigureAwait(true);

            await window.InvokeAsync("Done").ConfigureAwait(true);
            await window.UntilAsync(() => !VisualTree.Descendants(window.Content).OfType<TextBox>()
                .Any(box => Automation.NameOf(box) == "Find in terminal")).ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].ScrollOffset == 0
                && frame.Blocks[0].SearchQuery.Length == 0).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Commands opens one panel, finds an action by name, and runs it.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task CommandsRunsAnActionByName()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.MenuAsync("View", "Commands…").ConfigureAwait(true);
            ContentDialog commands = await window.DialogAsync("Commands").ConfigureAwait(true);
            await window.MenuAsync("View", "Commands…").ConfigureAwait(true);
            Assert.HasCount(1, VisualTree.Everything(window.Content).OfType<ContentDialog>().ToList(),
                "Opening Commands again opened a second panel.");
            TextBox search = await window.FindAsync<TextBox>("Search commands").ConfigureAwait(true);
            TestWindow.Enter(search, "split right");
            _ = await window.FindAsync<TextBlock>(text => text.Text == "Split Right").ConfigureAwait(true);
            await window.CloseDialogAsync(commands, "PrimaryButton").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks.Count == 2).ConfigureAwait(true);
            await window.UntilAsync(() => window.Surface.HasInputFocus).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// A new session opens from its dialog, and the session menu switches back to the first.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task SessionsAreCreatedAndSwitched()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            DesktopFrame initial = window.Frame;
            await window.MenuAsync("Session", "New Session…").ConfigureAwait(true);
            ContentDialog dialog = await window.DialogAsync("New Session").ConfigureAwait(true);
            TestWindow.Enter(await window.FindAsync<TextBox>("Session name").ConfigureAwait(true), "Windows session");
            await window.DelayAsync(100).ConfigureAwait(true);
            await window.CloseDialogAsync(dialog, "PrimaryButton").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Title == "Windows session" && frame.Sessions.Count == 2
                && frame.Blocks.Count == 1).ConfigureAwait(true);

            await window.InvokeAsync("Sessions").ConfigureAwait(true);
            await window.RunMenuAsync(initial.Title).ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.ActiveSession == initial.ActiveSession
                && frame.Blocks.Any(block => block.Id == initial.Blocks[0].Id)).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Tabs and panes are renamed through their dialogs.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task TabsAndPanesAreRenamed()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await RenameAsync(window, "Tab", "Rename Tab…", "Rename Tab", "Build tab").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Tabs[0].Name == "Build tab").ConfigureAwait(true);
            await window.UntilAsync(() => Automation.NameOf(window.TabItems()[0]) == "Build tab").ConfigureAwait(true);
            await RenameAsync(window, "Pane", "Rename Pane…", "Rename Pane", "Logs").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].Title == "Logs").ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Broadcast input is confirmed, reaches every pane in the tab, and stops when turned off.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task BroadcastReachesEveryPaneUntilTurnedOff()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.MenuAsync("Pane", "Split Right").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks.Count == 2
                && frame.Blocks.All(block => TestWindow.Text(block).TrimEnd().EndsWith('$'))).ConfigureAwait(true);
            await SetBroadcastAsync(window).ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Tabs[0].Synchronized).ConfigureAwait(true);
            await window.RunAsync(@"print 'SHARED\055INPUT\n'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks.All(block =>
                TestWindow.Text(block).Contains("SHARED-INPUT", StringComparison.Ordinal))).ConfigureAwait(true);

            await SetBroadcastAsync(window).ConfigureAwait(true);
            _ = await window.WaitAsync(frame => !frame.Tabs[0].Synchronized).ConfigureAwait(true);
            await window.RunAsync(@"print 'SINGLE\055INPUT\n'").ConfigureAwait(true);
            DesktopFrame single = await window.WaitAsync(frame =>
                TestWindow.Text(frame.Blocks.Single(block => block.Active))
                    .Contains("SINGLE-INPUT", StringComparison.Ordinal)).ConfigureAwait(true);
            await window.DelayAsync(300).ConfigureAwait(true);
            Assert.DoesNotContain("SINGLE", TestWindow.Text(window.Frame.Blocks.Single(block => !block.Active)),
                "Input still reached the other pane after broadcasting stopped.");
            Assert.IsNotNull(single);
        });
    }

    /// <summary>
    /// Closing the window leaves the session running, and a new window reattaches to its panes.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task ANewWindowReattaches()
    {
        return DesktopApp.RunAsync(async () =>
        {
            PrivateServer server = await PrivateServer.CreateAsync().ConfigureAwait(true);
            await using (server.ConfigureAwait(true))
            {
                string session;
                string pane;
                TestWindow first = await TestWindow.OpenReadyAsync(TestContext.CancellationToken).ConfigureAwait(true);
                await using (first.ConfigureAwait(true))
                {
                    await first.MenuAsync("Pane", "Split Right").ConfigureAwait(true);
                    DesktopFrame split = await first.WaitAsync(frame => frame.Blocks.Count == 2).ConfigureAwait(true);
                    session = split.ActiveSession!;
                    pane = split.Blocks[0].Id;
                    await first.RunAsync(@"print 'STILL\055RUNNING\n'").ConfigureAwait(true);
                    _ = await first.WaitAsync(frame =>
                        TestWindow.Text(frame).Contains("STILL-RUNNING", StringComparison.Ordinal))
                        .ConfigureAwait(true);
                }

                TestWindow second = await TestWindow.OpenAsync(TestContext.CancellationToken).ConfigureAwait(true);
                await using (second.ConfigureAwait(true))
                {
                    _ = await second.WaitAsync(frame => frame.ActiveSession == session && frame.Blocks.Count == 2
                        && frame.Blocks.Any(block => block.Id == pane)
                        && TestWindow.Text(frame).Contains("STILL-RUNNING", StringComparison.Ordinal))
                        .ConfigureAwait(true);
                }
            }
        });
    }

    /// <summary>
    /// A narrow window keeps the tabs and the new tab button inside the title bar.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task NarrowWindowsKeepTheTabControls()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            int wide = window.Active.Width;
            window.Window.SetSize(520, 340);
            _ = await window.WaitAsync(frame => frame.Blocks[0].Width < wide).ConfigureAwait(true);
            await window.SettleAsync().ConfigureAwait(true);
            Button add = VisualTree.Descendants(window.Tabs).OfType<Button>()
                .Single(button => button.Name == "AddButton");
            Rect bounds = add.TransformToVisual(null)
                .TransformBounds(new Rect(0, 0, add.ActualWidth, add.ActualHeight));
            Assert.IsGreaterThan(0, add.ActualWidth, "The new tab button collapsed.");
            Assert.IsLessThanOrEqualTo(window.Content.ActualSize.X, (float)bounds.Right,
                "The new tab button moved outside the window.");
            Assert.IsGreaterThan(0, window.TabItems()[0].ActualWidth, "The tab collapsed.");
        });
    }

    private static async Task AddTabAsync(TestWindow window)
    {
        Button add = VisualTree.Descendants(window.Tabs).OfType<Button>().Single(button => button.Name == "AddButton");
        Automation.Invoke(add);
        await window.DelayAsync(50).ConfigureAwait(true);
    }

    private static Button CloseButton(TabViewItem item)
    {
        return VisualTree.Descendants(item).OfType<Button>().Single(button => button.Name == "CloseButton");
    }

    private static async Task RenameAsync(TestWindow window, string menu, string item, string title, string name)
    {
        await window.MenuAsync(menu, item).ConfigureAwait(true);
        ContentDialog dialog = await window.DialogAsync(title).ConfigureAwait(true);
        TestWindow.Enter(await window.FindAsync<TextBox>("Name").ConfigureAwait(true), name);
        await window.DelayAsync(100).ConfigureAwait(true);
        await window.CloseDialogAsync(dialog, "PrimaryButton").ConfigureAwait(true);
    }

    private static async Task SetBroadcastAsync(TestWindow window)
    {
        await window.MenuAsync("Pane", "Broadcast Input…").ConfigureAwait(true);
        ContentDialog dialog = await window.DialogAsync("Change input broadcasting?").ConfigureAwait(true);
        await window.CloseDialogAsync(dialog, "PrimaryButton").ConfigureAwait(true);
    }
}
