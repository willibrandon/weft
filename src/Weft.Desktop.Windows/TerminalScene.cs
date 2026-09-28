using Weft.Client;
using Windows.UI;

namespace Weft.Desktop.Windows;

/// <summary>
/// The immutable inputs for one drawing pass over the terminal surface.
/// </summary>
/// <param name="Frame">The latest desktop frame.</param>
/// <param name="Background">The terminal background.</param>
/// <param name="Foreground">The default text color.</param>
/// <param name="Cursor">The cursor color.</param>
/// <param name="Selection">The selection highlight.</param>
/// <param name="CursorLit">Whether a blinking cursor is in its visible phase.</param>
/// <param name="Inset">The margin around the cell grid.</param>
internal sealed record TerminalScene(DesktopFrame Frame, Color Background, Color Foreground, Color Cursor,
    Color Selection, bool CursorLit, float Inset);
