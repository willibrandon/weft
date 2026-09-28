using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using Weft.Client;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// A real terminal window, driven through the entry points that keyboard, pointer, and automation input reach.
/// </summary>
internal sealed class TestWindow : IAsyncDisposable
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TestWindow(ReactorWindow window, TerminalSurface surface, CancellationToken cancellationToken)
    {
        Window = window;
        Surface = surface;
        CancellationToken = cancellationToken;
        window.Closed += (_, _) => _closed.TrySetResult();
    }

    /// <summary>
    /// Gets the Reactor window.
    /// </summary>
    internal ReactorWindow Window { get; }

    /// <summary>
    /// Gets the terminal surface.
    /// </summary>
    internal TerminalSurface Surface { get; }

    /// <summary>
    /// Gets the token that cancels the test.
    /// </summary>
    internal CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the window's content.
    /// </summary>
    internal UIElement Content => Window.NativeWindow?.Content
        ?? throw new InvalidOperationException("The window closed.");

    /// <summary>
    /// Gets the hidden text box that receives typed and composed text.
    /// </summary>
    internal TerminalInput Input => VisualTree.Descendants(Surface).OfType<TerminalInput>().Single();

    /// <summary>
    /// Gets the latest frame.
    /// </summary>
    internal DesktopFrame Frame => Surface.Frame ?? throw new AssertFailedException("No frame has arrived.");

    /// <summary>
    /// Gets the active pane in the latest frame.
    /// </summary>
    internal DesktopBlockFrame Active => Frame.Blocks.Single(block => block.Active);

    /// <summary>
    /// Gets the title bar's tab strip.
    /// </summary>
    internal TabView Tabs => VisualTree.Descendants(Content).OfType<TabView>().Single();

    /// <summary>
    /// Gets whether a dialog or menu is open over the window.
    /// </summary>
    internal bool HasPopup => Content.XamlRoot is { } root && VisualTree.OpenPopups(root).Count != 0;

    /// <summary>
    /// Gets the window handle.
    /// </summary>
    internal nint Handle => Win32Interop.GetWindowFromWindowId(Window.AppWindow.Id);

    /// <summary>
    /// Opens a terminal window as New Window does, without taking the foreground, and waits for its surface.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The window.</returns>
    internal static async Task<TestWindow> OpenAsync(CancellationToken cancellationToken)
    {
        ReactorWindow window = DesktopWindows.OpenTerminal(background: true);
        TerminalSurface? surface = null;
        await UntilAsync(() => (surface = window.NativeWindow?.Content is { } content
            ? VisualTree.Descendants(content).OfType<TerminalSurface>().FirstOrDefault()
            : null) is not null, "the terminal surface", cancellationToken).ConfigureAwait(true);
        return new TestWindow(window, surface!, cancellationToken);
    }

    /// <summary>
    /// Opens a window and waits for the test shell's first prompt.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The window.</returns>
    internal static async Task<TestWindow> OpenReadyAsync(CancellationToken cancellationToken)
    {
        TestWindow window = await OpenAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            _ = await window.WaitForPromptAsync().ConfigureAwait(true);
            // The status line leaves once connected, which gives the terminal its final rows.
            await UntilAsync(() => !VisualTree.Descendants(window.Content).OfType<TextBlock>()
                .Any(text => Automation.NameOf(text) == "Connection status"), "the connection status to clear",
                cancellationToken).ConfigureAwait(true);
            await window.SettleAsync().ConfigureAwait(true);
            // A lone pane spans the window's columns inside the one-cell frame on each side.
            _ = await window.WaitAsync(frame => frame.Blocks.Count != 1
                || frame.Blocks[0].Width + 2 == window.Surface.CellGrid.Columns).ConfigureAwait(true);
        }
        catch
        {
            await window.DisposeAsync().ConfigureAwait(true);
            throw;
        }

        return window;
    }

    /// <summary>
    /// Waits for a condition on the UI thread without blocking it.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="description">What the wait is for, reported on timeout.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    internal static async Task UntilAsync(Func<bool> condition, string description, CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)s_timeout.TotalMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("Timed out waiting for " + description + ".");
            }

            await Task.Delay(15, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Waits for a condition in this window.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="description">The condition's source text.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    internal Task UntilAsync(Func<bool> condition,
        [CallerArgumentExpression(nameof(condition))] string description = "")
    {
        return UntilAsync(condition, description, CancellationToken);
    }

    /// <summary>
    /// Waits for a frame that satisfies a condition, failing on a reported error.
    /// </summary>
    /// <param name="predicate">The condition.</param>
    /// <param name="condition">The condition's source text.</param>
    /// <param name="line">The calling line.</param>
    /// <returns>The frame.</returns>
    internal Task<DesktopFrame> WaitAsync(Func<DesktopFrame, bool> predicate,
        [CallerArgumentExpression(nameof(predicate))] string condition = "", [CallerLineNumber] int line = 0)
    {
        return WaitAsync(predicate, failOnError: true, condition, line);
    }

    /// <summary>
    /// Waits for a frame that satisfies a condition while the window reports an expected error, such as reconnecting.
    /// </summary>
    /// <param name="predicate">The condition.</param>
    /// <param name="condition">The condition's source text.</param>
    /// <param name="line">The calling line.</param>
    /// <returns>The frame.</returns>
    internal Task<DesktopFrame> WaitThroughErrorsAsync(Func<DesktopFrame, bool> predicate,
        [CallerArgumentExpression(nameof(predicate))] string condition = "", [CallerLineNumber] int line = 0)
    {
        return WaitAsync(predicate, failOnError: false, condition, line);
    }

    private async Task<DesktopFrame> WaitAsync(Func<DesktopFrame, bool> predicate, bool failOnError, string condition,
        int line)
    {
        long deadline = Environment.TickCount64 + (long)s_timeout.TotalMilliseconds;
        while (true)
        {
            if (Surface.Frame is { } frame)
            {
                if (failOnError && frame.Error is { } error)
                {
                    Assert.Fail("The window reported an error while waiting for " + condition + ": " + error);
                }

                if (predicate(frame))
                {
                    return frame;
                }
            }

            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("Timed out at line " + line + " waiting for " + condition + ". " + Describe(Surface.Frame));
            }

            await Task.Delay(15, CancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Waits briefly without expecting any change, for checks that something did not happen.
    /// </summary>
    /// <param name="milliseconds">The delay.</param>
    /// <returns>A task that completes after the delay.</returns>
    internal Task DelayAsync(int milliseconds)
    {
        return Task.Delay(milliseconds, CancellationToken);
    }

    /// <summary>
    /// Gets the text of every visible pane.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The concatenated cell text.</returns>
    internal static string Text(DesktopFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return string.Concat(frame.Blocks.Select(Text));
    }

    /// <summary>
    /// Gets a pane's text in row-major order.
    /// </summary>
    /// <param name="block">The pane.</param>
    /// <returns>The concatenated cell text.</returns>
    internal static string Text(DesktopBlockFrame block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return string.Concat(block.Cells.Select(cell => cell.Text));
    }

    /// <summary>
    /// Finds the cell index where text starts in a pane.
    /// </summary>
    /// <param name="block">The pane.</param>
    /// <param name="text">Text that fits on one row.</param>
    /// <returns>The row-major index of its first cell.</returns>
    internal static int IndexOf(DesktopBlockFrame block, string text)
    {
        ArgumentNullException.ThrowIfNull(block);
        for (int index = 0; index + text.Length <= block.Cells.Count; index++)
        {
            if (Enumerable.Range(0, text.Length)
                .All(offset => block.Cells[index + offset].Text == text[offset..(offset + 1)]))
            {
                return index;
            }
        }

        throw new AssertFailedException("The pane does not show " + text + ".");
    }

    /// <summary>
    /// Types text the way a keyboard does, through the terminal's text box.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the terminal has sent the text.</returns>
    internal async Task TypeAsync(string text)
    {
        TerminalInput input = Input;
        input.Text = text;
        // Text boxes report changes asynchronously; the terminal empties the box once it sends the text.
        await UntilAsync(() => input.Text.Length == 0, "typed text to be sent", CancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Replaces a text field's contents, which raises the same change event as typing.
    /// </summary>
    /// <param name="field">The text field.</param>
    /// <param name="text">The text.</param>
    internal static void Enter(TextBox field, string text)
    {
        ArgumentNullException.ThrowIfNull(field);
        field.Text = text;
    }

    /// <summary>
    /// Presses a key that produces no text.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The Ctrl, Alt, and Shift state.</param>
    internal void Press(VirtualKey key, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None)
    {
        Assert.IsTrue(Surface.HandleKey(key, modifiers, altGraph: false), "The terminal did not consume " + key + ".");
    }

    /// <summary>
    /// Types a shell command and presses Enter.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>A task that completes when the command has been sent.</returns>
    internal async Task RunAsync(string command)
    {
        await TypeAsync(command).ConfigureAwait(true);
        Press(VirtualKey.Enter);
    }

    /// <summary>
    /// Waits for the test shell's prompt at the end of the active pane.
    /// </summary>
    /// <returns>The frame with the prompt.</returns>
    internal Task<DesktopFrame> WaitForPromptAsync()
    {
        return WaitAsync(frame => frame.Blocks.FirstOrDefault(block => block.Active) is { } block
            && Text(block).TrimEnd().EndsWith('$'));
    }

    /// <summary>
    /// Gets the center of a cell in surface coordinates.
    /// </summary>
    /// <param name="block">The pane.</param>
    /// <param name="index">The row-major cell index.</param>
    /// <returns>The point.</returns>
    internal Point CellCenter(DesktopBlockFrame block, int index)
    {
        Rect cell = Surface.CellRect(block, index);
        return new Point(cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
    }

    /// <summary>
    /// Clicks the left button at a point without moving.
    /// </summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <param name="modifiers">The Ctrl, Alt, and Shift state.</param>
    internal void Click(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None)
    {
        _ = Surface.PointerDown(point, 1, modifiers);
        Surface.PointerUp(point, modifiers);
    }

    /// <summary>
    /// Presses the left button, moves through each point, and releases at the last one.
    /// </summary>
    /// <param name="points">The start followed by the drag path.</param>
    internal void Drag(params Point[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _ = Surface.PointerDown(points[0], 1, VirtualKeyModifiers.None);
        foreach (Point point in points.Skip(1))
        {
            Surface.PointerMove(point, leftButton: true, contact: true, VirtualKeyModifiers.None);
        }

        Surface.PointerUp(points[^1], VirtualKeyModifiers.None);
    }

    /// <summary>
    /// Finds a shown control in the window or its popups.
    /// </summary>
    /// <typeparam name="T">The control type.</typeparam>
    /// <param name="predicate">The condition.</param>
    /// <param name="description">The condition's source text.</param>
    /// <returns>The control.</returns>
    internal async Task<T> FindAsync<T>(Func<T, bool> predicate,
        [CallerArgumentExpression(nameof(predicate))] string description = "")
        where T : FrameworkElement
    {
        T? found = null;
        await UntilAsync(() => (found = VisualTree.Everything(Content).OfType<T>()
            .FirstOrDefault(element => element.Visibility == Visibility.Visible && element.ActualWidth > 0
                && predicate(element))) is not null, typeof(T).Name + " where " + description, CancellationToken)
            .ConfigureAwait(true);
        return found!;
    }

    /// <summary>
    /// Finds a shown control by the name automation clients read.
    /// </summary>
    /// <typeparam name="T">The control type.</typeparam>
    /// <param name="name">The name.</param>
    /// <returns>The control.</returns>
    internal Task<T> FindAsync<T>(string name)
        where T : FrameworkElement
    {
        return FindAsync<T>(element => Automation.NameOf(element) == name, name);
    }

    /// <summary>
    /// Invokes a button or menu item by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>A task that completes after the invocation.</returns>
    internal async Task InvokeAsync(string name)
    {
        Control control = await FindAsync<Control>(name).ConfigureAwait(true);
        Automation.Invoke(control);
    }

    /// <summary>
    /// Runs a command from the title bar's actions menu, opening each submenu on the path.
    /// </summary>
    /// <param name="path">Submenu names followed by the item's name.</param>
    /// <returns>A task that completes when the menu has closed.</returns>
    internal async Task MenuAsync(params string[] path)
    {
        ArgumentNullException.ThrowIfNull(path);
        await InvokeAsync("Terminal actions").ConfigureAwait(true);
        await RunMenuAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs an item from a menu that is already open.
    /// </summary>
    /// <param name="path">Submenu names followed by the item's name.</param>
    /// <returns>A task that completes when the menu has closed.</returns>
    internal async Task RunMenuAsync(params string[] path)
    {
        ArgumentNullException.ThrowIfNull(path);
        foreach (string submenu in path[..^1])
        {
            Automation.Expand(await FindAsync<MenuFlyoutSubItem>(item => item.Text == submenu).ConfigureAwait(true));
        }

        MenuFlyoutItem target = await FindAsync<MenuFlyoutItem>(item => item.Text == path[^1]).ConfigureAwait(true);
        Automation.Activate(target);
        await UntilAsync(() => !VisualTree.Everything(Content).OfType<MenuFlyoutPresenter>().Any(),
            "the menu to close", CancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Waits for a dialog with a title.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The dialog.</returns>
    internal Task<ContentDialog> DialogAsync(string title)
    {
        return FindAsync<ContentDialog>(dialog => (dialog.Title as string) == title);
    }

    /// <summary>
    /// Presses a dialog button: <c>PrimaryButton</c>, <c>SecondaryButton</c>, or <c>CloseButton</c>.
    /// </summary>
    /// <param name="dialog">The dialog.</param>
    /// <param name="part">The button's template name.</param>
    /// <returns>A task that completes when the dialog has closed.</returns>
    internal async Task CloseDialogAsync(ContentDialog dialog, string part)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => closed.TrySetResult();
        Button button = VisualTree.Descendants(dialog).OfType<Button>().Single(item => item.Name == part);
        Automation.Invoke(button);
        // The window handles the result when the dialog reports it closed, after its closing animation.
        await closed.Task.WaitAsync(s_timeout, CancellationToken).ConfigureAwait(true);
        await SettleAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Gets the tab items in order.
    /// </summary>
    /// <returns>The tabs.</returns>
    internal TabViewItem[] TabItems()
    {
        return [.. VisualTree.Descendants(Tabs).OfType<TabViewItem>()];
    }

    /// <summary>
    /// Waits for the terminal canvas to draw and the compositor to present it.
    /// </summary>
    /// <returns>A task that completes after several composed frames.</returns>
    internal async Task SettleAsync()
    {
        int frames = 0;
        void Count(object? sender, object e)
        {
            frames++;
        }

        CompositionTarget.Rendering += Count;
        try
        {
            await UntilAsync(() => frames >= 4, "composed frames", CancellationToken).ConfigureAwait(true);
        }
        finally
        {
            CompositionTarget.Rendering -= Count;
        }

        await Task.Delay(60, CancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Copies the window's composed pixels after drawing settles.
    /// </summary>
    /// <returns>The capture.</returns>
    internal async Task<WindowCapture> CaptureAsync()
    {
        await SettleAsync().ConfigureAwait(true);
        return await WindowCapture.TakeAsync(Handle, (ulong)Handle, CancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Converts a surface point to a pixel in window captures.
    /// </summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <returns>The pixel.</returns>
    internal (int X, int Y) PixelOf(Point point)
    {
        Point window = Surface.TransformToVisual(null).TransformPoint(point);
        double scale = Surface.XamlRoot.RasterizationScale;
        return ((int)(window.X * scale), (int)(window.Y * scale));
    }

    /// <summary>
    /// Samples a captured pixel at a surface point.
    /// </summary>
    /// <param name="capture">The capture.</param>
    /// <param name="point">The point in surface coordinates.</param>
    /// <returns>The color.</returns>
    internal Color PixelAt(WindowCapture capture, Point point)
    {
        ArgumentNullException.ThrowIfNull(capture);
        (int x, int y) = PixelOf(point);
        return capture.At(x, y);
    }

    /// <summary>
    /// Gets the pixel rectangle the surface covers in window captures.
    /// </summary>
    /// <returns>The left, top, right, and bottom edges.</returns>
    internal (int Left, int Top, int Right, int Bottom) SurfacePixels()
    {
        (int left, int top) = PixelOf(new Point(0, 0));
        (int right, int bottom) = PixelOf(new Point(Surface.ActualWidth, Surface.ActualHeight));
        return (left, top, right, bottom);
    }

    /// <summary>
    /// Closes the window, leaving its session running.
    /// </summary>
    /// <returns>A task that completes when the window has closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (!_closed.Task.IsCompleted)
        {
            Window.Close();
        }

        await _closed.Task.WaitAsync(s_timeout, CancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Gets whether two measurements agree within rounding, such as sizes and scroll positions.
    /// </summary>
    /// <param name="first">The first value.</param>
    /// <param name="second">The second value.</param>
    /// <returns>Whether they differ by less than a hundredth.</returns>
    internal static bool Near(double first, double second)
    {
        return Math.Abs(first - second) < 0.01;
    }

    /// <summary>
    /// Gets how far two colors are apart, summed over their channels.
    /// </summary>
    /// <param name="first">The first color.</param>
    /// <param name="second">The second color.</param>
    /// <returns>The distance from 0 to 765.</returns>
    internal static int Distance(Color first, Color second)
    {
        return Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B);
    }

    private static string Describe(DesktopFrame? frame)
    {
        if (frame is null)
        {
            return "No frame arrived.";
        }

        IEnumerable<string> blocks = frame.Blocks.Select(block => string.Concat(
            block.Id, block.Active ? "*" : string.Empty, " ", block.Width, "x", block.Height, " at ", block.X, ",",
            block.Y, " cursor ", block.CursorX, ",", block.CursorY, " history ", block.HistoryLines, " offset ",
            block.ScrollOffset, " find ", block.SearchQuery, ":", block.SearchMatches,
            " [", Text(block).TrimEnd(), "]"));
        return "Connected " + frame.Connected + ", " + frame.Tabs.Count + " tabs, " + frame.Sessions.Count
            + " sessions: " + string.Join("; ", blocks);
    }
}
