using System.Text;
using Weft.Client;
using Windows.System;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Verifies typed, pressed, composed, and pasted input reaches a real shell exactly once.
/// </summary>
[TestClass]
public sealed class TerminalInputTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Types a command whose output has true color and a wide character, and checks the resulting cells.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task TypedCommandRunsInTheShell()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            Assert.HasCount(1, window.Frame.Blocks);
            await window.RunAsync(@"print '\033[2J\033[HWindows terminal: \033[38;2;12;200;160m界é\033[0m\n"
                + @"weft\055windows\055ok\n'").ConfigureAwait(true);
            DesktopFrame rendered = await window.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("weft-windows-ok", StringComparison.Ordinal)
                && frame.Blocks[0].Cells.Any(cell => cell is { Text: "界", Foreground: 0x0cc8a0 }))
                .ConfigureAwait(true);
            DesktopBlockFrame block = rendered.Blocks[0];
            int wide = TestWindow.IndexOf(block, "界");
            Assert.AreEqual(string.Empty, block.Cells[wide + 1].Text, "A wide character occupies two cells.");
            Assert.AreEqual("é", block.Cells[wide + 2].Text, "A combined character stays in one cell.");
        });
    }

    /// <summary>
    /// Presses keys that produce no text and checks the bytes the shell receives in raw mode.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task KeysArriveAsTerminalSequences()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"raw; print 'KEYS\055READY\n'; bytes 6; cooked").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => TestWindow.Text(frame).Contains("KEYS-READY", StringComparison.Ordinal))
                .ConfigureAwait(true);
            window.Press(VirtualKey.Up);
            window.Press(VirtualKey.A, VirtualKeyModifiers.Control);
            window.Press(VirtualKey.Tab);
            window.Press(VirtualKey.Back);
            _ = await window.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("27 91 65 1 9 127", StringComparison.Ordinal)).ConfigureAwait(true);
            Assert.IsFalse(window.Surface.HandleKey(VirtualKey.A, VirtualKeyModifiers.None, altGraph: false),
                "Printable keys arrive as text, not as named keys.");
            Assert.IsFalse(window.Surface.HandleKey(VirtualKey.Q,
                VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, altGraph: true),
                "AltGr characters follow the keyboard layout through text input.");
        });
    }

    /// <summary>
    /// Holds composed text locally, sends committed text once, and never sends a cancelled composition.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task CompositionCommitsExactlyOnce()
    {
        return DesktopApp.RunWindowAsync(TestContext, async (window, server) =>
        {
            const string Committed = "é界😀";
            string file = Path.Join(server.Root, "composition.bin");
            int length = Encoding.UTF8.GetByteCount(Committed);
            await window.RunAsync($@"raw; print 'IME\055READY\n'; record {length} '{file}'; cooked; "
                + @"print 'IME\055DONE\n'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => TestWindow.Text(frame).Contains("IME-READY", StringComparison.Ordinal))
                .ConfigureAwait(true);
            TerminalInput input = window.Input;

            // An input method that cancels clears its text before ending the composition.
            window.Surface.BeginComposition();
            input.Text = "取り消し";
            await window.DelayAsync(50).ConfigureAwait(true);
            Assert.AreEqual(1, input.Opacity, "Composed text shows at the caret while it is being composed.");
            input.Text = string.Empty;
            await window.DelayAsync(50).ConfigureAwait(true);
            window.Surface.EndComposition();
            await window.DelayAsync(150).ConfigureAwait(true);
            Assert.AreEqual(0, input.Opacity, "The input hides again after composition.");
            Assert.AreEqual(0, new FileInfo(file).Length, "A cancelled composition reached the process.");

            window.Surface.BeginComposition();
            input.Text = "ab";
            await window.DelayAsync(50).ConfigureAwait(true);
            input.Text = Committed;
            await window.DelayAsync(100).ConfigureAwait(true);
            Assert.AreEqual(0, new FileInfo(file).Length, "Uncommitted text reached the process.");
            DesktopBlockFrame block = window.Active;
            double caret = window.Surface.CellRect(block, (block.CursorY * block.Width) + block.CursorX).X;
            Assert.AreEqual(caret, Microsoft.UI.Xaml.Controls.Canvas.GetLeft(input), 0.5,
                "Candidate windows anchor to the terminal caret.");
            window.Surface.EndComposition();
            _ = await window.WaitAsync(frame => TestWindow.Text(frame).Contains("IME-DONE", StringComparison.Ordinal))
                .ConfigureAwait(true);
            Assert.AreSequenceEqual(Encoding.UTF8.GetBytes(Committed), await File.ReadAllBytesAsync(file,
                window.CancellationToken).ConfigureAwait(true), "Committed text changed on its way to the process.");
            await window.DelayAsync(200).ConfigureAwait(true);
            Assert.DoesNotContain("unknown command", TestWindow.Text(window.Frame), "Committed text arrived twice.");
            Assert.AreEqual(string.Empty, input.Text);
        });
    }

    /// <summary>
    /// Discards a composition when another pane becomes active before it commits.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task CompositionIsCancelledWhenThePaneChanges()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            string first = window.Active.Id;
            window.Surface.BeginComposition();
            window.Input.Text = "lost";
            await window.DelayAsync(50).ConfigureAwait(true);
            await window.MenuAsync("Pane", "Split Right").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks.Count == 2
                && frame.Blocks.Single(block => block.Active).Id != first).ConfigureAwait(true);
            // Moving focus ends the input method's composition, which reports its text as committed.
            window.Surface.EndComposition();
            await window.DelayAsync(300).ConfigureAwait(true);
            Assert.DoesNotContain("lost", TestWindow.Text(window.Frame), "Text composed in one pane reached another.");
            Assert.AreEqual(string.Empty, window.Input.Text);
            await window.RunAsync(@"print 'INPUT\055WORKS\n'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => TestWindow.Text(frame.Blocks.Single(block => block.Active))
                .Contains("INPUT-WORKS", StringComparison.Ordinal)).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Pastes Windows line endings as one newline per line, running each pasted command once.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task PastedLinesRunOnce()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print '\033[2J\033[H'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => TestWindow.Text(frame).Trim() == "$").ConfigureAwait(true);
            window.Surface.PasteText("print 'PASTE\\055ONE\\n'\r\nprint 'PASTE\\055TWO\\n'\r\n");
            DesktopFrame pasted = await window.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("PASTE-TWO", StringComparison.Ordinal)
                && TestWindow.Text(frame).TrimEnd().EndsWith('$')).ConfigureAwait(true);
            DesktopBlockFrame block = pasted.Blocks[0];
            int prompts = Enumerable.Range(0, block.Height)
                .Select(row => string.Concat(block.Cells.Skip(row * block.Width).Take(block.Width)
                    .Select(cell => cell.Text)))
                .Count(line => line.Trim() == "$");
            Assert.AreEqual(1, prompts, "A pasted line ending entered an extra empty command.");
            Assert.Contains("PASTE-ONE", TestWindow.Text(block));
        });
    }
}
