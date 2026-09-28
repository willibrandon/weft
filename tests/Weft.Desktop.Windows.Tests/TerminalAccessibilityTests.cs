using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;
using Weft.Client;
using Windows.Foundation;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Queries the terminal's UI Automation text pattern against Unicode received from a real process.
/// </summary>
[TestClass]
public sealed class TerminalAccessibilityTests
{
    private const int IsReadOnlyAttribute = 40015;

    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Text, character and line ranges, bounds, and hit testing agree with the terminal's cells.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task TextPatternMatchesTheCells()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print '\033[2J\033[HAccessible terminal café 界é\n'").ConfigureAwait(true);
            _ = await window.WaitAsync(frame =>
                TestWindow.Text(frame).StartsWith("Accessible terminal café 界é", StringComparison.Ordinal))
                .ConfigureAwait(true);
            AutomationPeer peer = FrameworkElementAutomationPeer.CreatePeerForElement(window.Input);
            Assert.AreEqual(AutomationControlType.Document, peer.GetAutomationControlType());
            Assert.AreEqual("Terminal", peer.GetName());
            ITextProvider text = Automation.Pattern<ITextProvider>(window.Input, PatternInterface.Text);
            ITextRangeProvider document = text.DocumentRange;
            Assert.StartsWith("Accessible terminal café 界é\n", document.GetText(-1));
            Assert.IsTrue((bool)document.GetAttributeValue(IsReadOnlyAttribute), "Terminal output must be read-only.");

            ITextRangeProvider combining = Find(document, "é");
            combining.ExpandToEnclosingUnit(TextUnit.Character);
            Assert.AreEqual("é", combining.GetText(-1));

            ITextRangeProvider line = Find(document, "terminal");
            line.ExpandToEnclosingUnit(TextUnit.Line);
            Assert.AreEqual("Accessible terminal café 界é", line.GetText(-1).TrimEnd('\n'));

            ITextRangeProvider word = Find(document, "caf");
            word.ExpandToEnclosingUnit(TextUnit.Word);
            Assert.AreEqual("café", word.GetText(-1).Trim());

            ITextRangeProvider wide = Find(document, "界");
            wide.GetBoundingRectangles(out double[] bounds);
            Assert.HasCount(4, bounds, "A single-row range has one rectangle.");
            double scale = window.Surface.XamlRoot.RasterizationScale;
            Assert.AreEqual(window.Surface.Font.CellWidth * 2 * scale, bounds[2], 1.5,
                "A wide character spans two cells.");
            ITextRangeProvider hit = text.RangeFromPoint(new Point(bounds[0] + (bounds[2] * 0.75),
                bounds[1] + (bounds[3] / 2)));
            hit.ExpandToEnclosingUnit(TextUnit.Character);
            Assert.AreEqual("界", hit.GetText(-1), "The right half of a wide cell belongs to its character.");
        });
    }

    /// <summary>
    /// Selecting through automation selects terminal text without moving the process cursor or typing.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task AccessibleSelectionLeavesTheProcessAlone()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print '\033[2J\033[HAccessible terminal café 界é\n'").ConfigureAwait(true);
            DesktopFrame shown = await window.WaitAsync(frame =>
                TestWindow.Text(frame).StartsWith("Accessible terminal café 界é", StringComparison.Ordinal)
                && TestWindow.Text(frame).TrimEnd().EndsWith('$')).ConfigureAwait(true);
            string screen = TestWindow.Text(shown);
            (int x, int y) = (shown.Blocks[0].CursorX, shown.Blocks[0].CursorY);
            ITextProvider text = Automation.Pattern<ITextProvider>(window.Input, PatternInterface.Text);
            ITextRangeProvider range = Find(text.DocumentRange, "界é");
            range.Select();
            DesktopFrame selected = await window.WaitAsync(frame => frame.Blocks[0].Selection?.Text == "界é")
                .ConfigureAwait(true);
            Assert.AreEqual((x, y), (selected.Blocks[0].CursorX, selected.Blocks[0].CursorY),
                "Selection moved the process cursor.");
            Assert.AreEqual("界é", text.GetSelection().Single().GetText(-1));
            Assert.AreEqual(screen, TestWindow.Text(selected), "Selecting changed the terminal's text.");

            ITextRangeProvider collapsed = range.Clone();
            collapsed.MoveEndpointByRange(TextPatternRangeEndpoint.End, collapsed, TextPatternRangeEndpoint.Start);
            collapsed.Select();
            _ = await window.WaitAsync(frame => frame.Blocks[0].Selection is null).ConfigureAwait(true);
        });
    }

    private static ITextRangeProvider Find(ITextRangeProvider range, string text)
    {
        return range.FindText(text, false, false)
            ?? throw new AssertFailedException("The text pattern has no " + text + ".");
    }
}
