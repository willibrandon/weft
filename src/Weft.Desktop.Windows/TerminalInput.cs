using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Weft.Desktop.Windows;

/// <summary>
/// Receives keyboard focus and text services for the terminal at its caret.
/// </summary>
/// <remarks>
/// Windows text services need an editable control, so a borderless text box follows the terminal caret.
/// Input methods place candidates against it, dead keys and AltGr produce text through it, and composition
/// shows inside it until committed. Committed text is forwarded to the terminal and removed immediately.
/// Screen readers see the terminal's own text through the surface's automation peer.
/// </remarks>
internal sealed partial class TerminalInput : TextBox
{
    private readonly TerminalSurface _surface;

    /// <summary>
    /// Creates the input for a surface.
    /// </summary>
    /// <param name="surface">The owning surface.</param>
    internal TerminalInput(TerminalSurface surface)
    {
        _surface = surface;
        AcceptsReturn = false;
        IsSpellCheckEnabled = false;
        IsTextPredictionEnabled = false;
        TextWrapping = TextWrapping.NoWrap;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        MinWidth = 0;
        MinHeight = 0;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ContextFlyout = null;
        SelectionHighlightColor = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Opacity = 0;
        TabIndex = 0;
        foreach (string key in (string[])["TextControlBackgroundFocused", "TextControlBackgroundPointerOver",
            "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver", "TextControlBorderBrush"])
        {
            Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new TerminalAutomationPeer(this, _surface);
    }
}
