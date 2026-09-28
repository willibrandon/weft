using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;
using System.Globalization;

namespace Weft.Desktop.Windows;

/// <summary>
/// A UTF-16 range over the active pane's text, measured against the text current at each call.
/// </summary>
internal sealed partial class TerminalTextRange : ITextRangeProvider
{
    private const int IsReadOnlyAttribute = 40015;
    private readonly TerminalAutomationPeer _peer;
    private int _start;
    private int _end;

    /// <summary>
    /// Creates a range.
    /// </summary>
    /// <param name="peer">The owning peer.</param>
    /// <param name="start">The first offset.</param>
    /// <param name="end">The offset after the range.</param>
    internal TerminalTextRange(TerminalAutomationPeer peer, int start, int end)
    {
        _peer = peer;
        _start = Math.Min(start, end);
        _end = Math.Max(start, end);
    }

    private string Text => _peer.Text?.Value ?? string.Empty;

    /// <inheritdoc />
    public ITextRangeProvider Clone()
    {
        return new TerminalTextRange(_peer, _start, _end);
    }

    /// <inheritdoc />
    public bool Compare(ITextRangeProvider textRangeProvider)
    {
        return textRangeProvider is TerminalTextRange other && other._start == _start && other._end == _end;
    }

    /// <inheritdoc />
    public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider textRangeProvider,
        TextPatternRangeEndpoint targetEndpoint)
    {
        var target = (TerminalTextRange)textRangeProvider;
        int mine = endpoint == TextPatternRangeEndpoint.Start ? _start : _end;
        int theirs = targetEndpoint == TextPatternRangeEndpoint.Start ? target._start : target._end;
        return mine.CompareTo(theirs);
    }

    /// <inheritdoc />
    public void ExpandToEnclosingUnit(TextUnit unit)
    {
        Clamp();
        if (unit is TextUnit.Page or TextUnit.Document)
        {
            _start = 0;
            _end = Text.Length;
            return;
        }

        _start = IsBoundary(_start, unit) ? _start : Previous(_start, unit);
        _end = Next(_start, unit);
    }

    /// <inheritdoc />
    public ITextRangeProvider? FindAttribute(int attributeId, object value, bool backward)
    {
        return attributeId == IsReadOnlyAttribute && value is true ? Clone() : null;
    }

    /// <inheritdoc />
    public ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase)
    {
        Clamp();
        string value = Text[_start.._end];
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int index = backward ? value.LastIndexOf(text, comparison) : value.IndexOf(text, comparison);
        return index < 0 ? null : new TerminalTextRange(_peer, _start + index, _start + index + text.Length);
    }

    /// <inheritdoc />
    public object GetAttributeValue(int attributeId)
    {
        return attributeId == IsReadOnlyAttribute ? true : DependencyProperty.UnsetValue;
    }

    /// <inheritdoc />
    public void GetBoundingRectangles(out double[] returnValue)
    {
        Clamp();
        returnValue = _peer.Bounds(_start, _end);
    }

    /// <inheritdoc />
    public IRawElementProviderSimple GetEnclosingElement()
    {
        return _peer.Provider();
    }

    /// <inheritdoc />
    public string GetText(int maxLength)
    {
        Clamp();
        string value = Text[_start.._end];
        return maxLength >= 0 && value.Length > maxLength ? value[..maxLength] : value;
    }

    /// <inheritdoc />
    public int Move(TextUnit unit, int count)
    {
        Clamp();
        bool degenerate = _start == _end;
        ExpandToEnclosingUnit(unit);
        int moved = MoveOffset(ref _start, unit, count);
        _end = degenerate ? _start : Next(_start, unit);
        return moved;
    }

    /// <inheritdoc />
    public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
    {
        Clamp();
        int moved;
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            moved = MoveOffset(ref _start, unit, count);
            _end = Math.Max(_end, _start);
        }
        else
        {
            moved = MoveOffset(ref _end, unit, count);
            _start = Math.Min(_start, _end);
        }

        return moved;
    }

    /// <inheritdoc />
    public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider textRangeProvider,
        TextPatternRangeEndpoint targetEndpoint)
    {
        var target = (TerminalTextRange)textRangeProvider;
        int offset = targetEndpoint == TextPatternRangeEndpoint.Start ? target._start : target._end;
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            _start = offset;
            _end = Math.Max(_end, _start);
        }
        else
        {
            _end = offset;
            _start = Math.Min(_start, _end);
        }
    }

    /// <inheritdoc />
    public void Select()
    {
        Clamp();
        _peer.Select(_start, _end);
    }

    /// <inheritdoc />
    public void AddToSelection()
    {
        Select();
    }

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        _peer.Select(0, 0);
    }

    /// <inheritdoc />
    public void ScrollIntoView(bool alignToTop)
    {
        // The exposed text is the visible viewport, which is always in view.
    }

    /// <inheritdoc />
    public IRawElementProviderSimple[] GetChildren()
    {
        return [];
    }

    private void Clamp()
    {
        int length = Text.Length;
        _start = Math.Clamp(_start, 0, length);
        _end = Math.Clamp(_end, _start, length);
    }

    private int MoveOffset(ref int offset, TextUnit unit, int count)
    {
        int moved = 0;
        while (moved < Math.Abs(count))
        {
            int next = count > 0 ? Next(offset, unit) : Previous(offset, unit);
            if (next == offset)
            {
                break;
            }

            offset = next;
            moved++;
        }

        return count > 0 ? moved : -moved;
    }

    private bool IsBoundary(int offset, TextUnit unit)
    {
        return offset == 0 || offset == Text.Length || Previous(Next(offset, unit), unit) == offset;
    }

    private int Next(int offset, TextUnit unit)
    {
        string text = Text;
        if (offset >= text.Length)
        {
            return text.Length;
        }

        switch (unit)
        {
            case TextUnit.Character:
                return offset + StringInfo.GetNextTextElementLength(text, offset);
            case TextUnit.Word:
                int index = offset;
                bool space = char.IsWhiteSpace(text[index]);
                while (index < text.Length && char.IsWhiteSpace(text[index]) == space)
                {
                    index++;
                }

                while (!space && index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                return index;
            case TextUnit.Page:
            case TextUnit.Document:
                return text.Length;
            case TextUnit.Format:
            case TextUnit.Line:
            case TextUnit.Paragraph:
            default:
                int newline = text.IndexOf('\n', offset);
                return newline < 0 ? text.Length : newline + 1;
        }
    }

    private int Previous(int offset, TextUnit unit)
    {
        string text = Text;
        if (offset <= 0)
        {
            return 0;
        }

        switch (unit)
        {
            case TextUnit.Character:
                int start = 0;
                while (start + StringInfo.GetNextTextElementLength(text, start) < offset)
                {
                    start += StringInfo.GetNextTextElementLength(text, start);
                }

                return start;
            case TextUnit.Word:
                int index = offset - 1;
                while (index > 0 && char.IsWhiteSpace(text[index]))
                {
                    index--;
                }

                while (index > 0 && !char.IsWhiteSpace(text[index - 1]))
                {
                    index--;
                }

                return index;
            case TextUnit.Page:
            case TextUnit.Document:
                return 0;
            case TextUnit.Format:
            case TextUnit.Line:
            case TextUnit.Paragraph:
            default:
                // At a line start, the previous boundary is the start of the line before it.
                int search = text[offset - 1] == '\n' ? offset - 2 : offset - 1;
                return search < 0 ? 0 : text.LastIndexOf('\n', search) + 1;
        }
    }
}
