using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Weft.Client;
using Windows.Foundation;

namespace Weft.Desktop.Windows;

/// <summary>
/// Maps a pane's retained history to a native scroll bar without letting arriving frames move a dragged thumb.
/// </summary>
internal sealed class TerminalScrollBar
{
    private const double BarWidth = 14;
    private readonly Action<int> _scroll;
    private int _history;
    private int? _requested;
    private bool _tracking;
    private bool _applying;

    /// <summary>
    /// Creates a scroll bar that reports requested history offsets.
    /// </summary>
    /// <param name="scroll">Receives the number of rows above live output.</param>
    internal TerminalScrollBar(Action<int> scroll)
    {
        _scroll = scroll;
        Control.Scroll += OnScroll;
        Control.ValueChanged += OnValueChanged;
        AutomationProperties.SetName(Control, "Terminal history");
    }

    /// <summary>
    /// Gets the native control.
    /// </summary>
    internal ScrollBar Control { get; } = new()
    {
        Orientation = Orientation.Vertical,
        IndicatorMode = ScrollingIndicatorMode.MouseIndicator,
        Minimum = 0,
        SmallChange = 1,
        Width = BarWidth
    };

    /// <summary>
    /// Mirrors the retained row count and position, placing the bar at the pane's right edge.
    /// </summary>
    /// <param name="block">The pane.</param>
    /// <param name="content">The pane's content rectangle.</param>
    internal void Update(DesktopBlockFrame block, Rect content)
    {
        Canvas.SetLeft(Control, content.Right - BarWidth);
        Canvas.SetTop(Control, content.Y);
        Control.Height = Math.Max(0, content.Height);
        Control.Visibility = block.HistoryLines == 0 || block.AlternateScreen
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (_requested == block.ScrollOffset || _history != block.HistoryLines)
        {
            _requested = null;
        }

        _history = block.HistoryLines;
        if (_tracking || _requested is not null)
        {
            return;
        }

        _applying = true;
        Control.Maximum = _history;
        Control.ViewportSize = block.Height;
        Control.LargeChange = Math.Max(1, block.Height - 1);
        Control.Value = _history - Math.Clamp(block.ScrollOffset, 0, _history);
        _applying = false;
    }

    /// <summary>
    /// Returns the thumb to live output after an explicit resume.
    /// </summary>
    internal void Follow()
    {
        _requested = null;
        _tracking = false;
        _applying = true;
        Control.Value = Control.Maximum;
        _applying = false;
    }

    private void OnScroll(object sender, ScrollEventArgs e)
    {
        // Only a dragged thumb holds its position against arriving frames.
        _tracking = !_applying && e.ScrollEventType == ScrollEventType.ThumbTrack;
    }

    private void OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Pointer input and UI Automation both change the value; frames applied here do not request scrolling.
        if (_applying)
        {
            return;
        }

        int offset = Math.Clamp(_history - (int)Math.Round(e.NewValue), 0, _history);
        if (_requested == offset)
        {
            return;
        }

        _requested = offset;
        _scroll(offset);
    }
}
