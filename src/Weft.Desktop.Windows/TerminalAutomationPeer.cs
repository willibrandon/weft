using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Weft.Desktop.Windows;

/// <summary>
/// Presents the active pane to UI Automation as read-only text with ranges, selection, and geometry.
/// </summary>
/// <remarks>
/// Ranges index the same cells the surface paints. Selecting a range highlights terminal output without
/// moving the process cursor, and the exposed text cannot be edited.
/// </remarks>
internal sealed partial class TerminalAutomationPeer : FrameworkElementAutomationPeer, ITextProvider
{
    private readonly TerminalSurface _surface;
    private string _lastText = string.Empty;

    /// <summary>
    /// Creates the peer for the terminal input.
    /// </summary>
    /// <param name="input">The focused input control.</param>
    /// <param name="surface">The surface that owns the text.</param>
    internal TerminalAutomationPeer(TerminalInput input, TerminalSurface surface)
        : base(input)
    {
        _surface = surface;
    }

    /// <summary>
    /// Gets the current text, or null before a pane is attached.
    /// </summary>
    internal TerminalText? Text => _surface.ActiveBlock is { } block ? new TerminalText(block) : null;

    /// <inheritdoc />
    public ITextRangeProvider DocumentRange => new TerminalTextRange(this, 0, Text?.Value.Length ?? 0);

    /// <inheritdoc />
    public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;

    /// <summary>
    /// Raises text and selection events for listening clients after a frame changes.
    /// </summary>
    /// <param name="selectionChanged">Whether the active selection changed.</param>
    internal void Refresh(bool selectionChanged)
    {
        string text = Text?.Value ?? string.Empty;
        if (!string.Equals(text, _lastText, StringComparison.Ordinal))
        {
            _lastText = text;
            if (ListenerExists(AutomationEvents.TextPatternOnTextChanged))
            {
                RaiseAutomationEvent(AutomationEvents.TextPatternOnTextChanged);
            }
        }

        if (selectionChanged && ListenerExists(AutomationEvents.TextPatternOnTextSelectionChanged))
        {
            RaiseAutomationEvent(AutomationEvents.TextPatternOnTextSelectionChanged);
        }
    }

    /// <summary>
    /// Selects cells covered by a text range through the shared terminal selection.
    /// </summary>
    /// <param name="start">The first UTF-16 offset.</param>
    /// <param name="end">The offset after the range.</param>
    internal void Select(int start, int end)
    {
        if (Text is not { } text)
        {
            return;
        }

        if (end <= start)
        {
            _surface.ClearSelection();
            return;
        }

        _surface.SelectCells(text.CellAt(start), text.CellAt(end - 1));
    }

    /// <summary>
    /// Gets screen rectangles for the rows a range covers, in physical pixels.
    /// </summary>
    /// <param name="start">The first UTF-16 offset.</param>
    /// <param name="end">The offset after the range.</param>
    /// <returns>Rectangles flattened as left, top, width, height.</returns>
    internal double[] Bounds(int start, int end)
    {
        if (Text is not { } text || _surface.XamlRoot is not { } root)
        {
            return [];
        }

        var bounds = new List<double>();
        GeneralTransform toWindow = _surface.TransformToVisual(null);
        double scale = root.RasterizationScale;
        Point origin = ClientOrigin(root);
        int first = text.CellAt(start);
        int last = end > start ? text.CellAt(end - 1) : first;
        for (int row = first / text.Block.Width; row <= last / text.Block.Width; row++)
        {
            int left = row == first / text.Block.Width ? first : row * text.Block.Width;
            int right = row == last / text.Block.Width ? last : ((row + 1) * text.Block.Width) - 1;
            Rect cells = _surface.CellRect(text.Block, left);
            Rect lastCell = _surface.CellRect(text.Block, right);
            cells.Union(lastCell);
            Rect window = toWindow.TransformBounds(cells);
            bounds.Add(origin.X + (window.X * scale));
            bounds.Add(origin.Y + (window.Y * scale));
            bounds.Add(window.Width * scale);
            bounds.Add(window.Height * scale);
        }

        return [.. bounds];
    }

    /// <summary>
    /// Gets the text offset of the cell at a screen point.
    /// </summary>
    /// <param name="screen">The point in physical screen pixels.</param>
    /// <returns>The offset, or null outside the pane.</returns>
    internal int? OffsetAt(Point screen)
    {
        if (Text is not { } text || _surface.XamlRoot is not { } root)
        {
            return null;
        }

        Point origin = ClientOrigin(root);
        double scale = root.RasterizationScale;
        var window = new Point((screen.X - origin.X) / scale, (screen.Y - origin.Y) / scale);
        Point local = _surface.TransformToVisual(null).Inverse.TransformPoint(window);
        int index = _surface.CellIndex(text.Block, local);
        return text.Cells[index].Start;
    }

    /// <inheritdoc />
    public ITextRangeProvider[] GetSelection()
    {
        if (Text is not { } text)
        {
            return [];
        }

        if (text.Block.Selection is not { } selection || selection.End < 0 || selection.Start >= text.Cells.Count)
        {
            return [new TerminalTextRange(this, text.Cursor, text.Cursor)];
        }

        (int start, _) = text.Cells[Math.Max(0, selection.Start)];
        (int lastStart, int lastLength) = text.Cells[Math.Min(text.Cells.Count - 1, selection.End)];
        return [new TerminalTextRange(this, start, lastStart + lastLength)];
    }

    /// <inheritdoc />
    public ITextRangeProvider[] GetVisibleRanges()
    {
        return [DocumentRange];
    }

    /// <inheritdoc />
    public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement)
    {
        return DocumentRange;
    }

    /// <inheritdoc />
    public ITextRangeProvider RangeFromPoint(Point screenLocation)
    {
        int offset = OffsetAt(screenLocation) ?? 0;
        return new TerminalTextRange(this, offset, offset);
    }

    /// <summary>
    /// Wraps this peer for a range's enclosing element.
    /// </summary>
    /// <returns>The provider.</returns>
    internal IRawElementProviderSimple Provider()
    {
        return ProviderFromPeer(this);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        return patternInterface == PatternInterface.Text ? this : null;
    }

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Document;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return "WeftTerminal";
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        return "Terminal";
    }

    /// <inheritdoc />
    protected override bool IsKeyboardFocusableCore()
    {
        return true;
    }

    private static Point ClientOrigin(XamlRoot root)
    {
        nint window = Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
        var origin = new NativePoint();
        _ = NativeMethods.ClientToScreen(window, ref origin);
        return new Point(origin.X, origin.Y);
    }
}
