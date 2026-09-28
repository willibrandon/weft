namespace Weft.Client;

/// <summary>
/// A bounded, ordered request from a native desktop window.
/// </summary>
/// <param name="Operation">The operation: text, key, paste, resize, tab, newTab, splitRight, splitBelow, focus, or zoom.</param>
/// <param name="Target">The explicit tab or block id, when applicable.</param>
/// <param name="Text">The text or named key to send.</param>
/// <param name="Width">The viewport columns for resize.</param>
/// <param name="Height">The viewport rows for resize.</param>
/// <param name="X">The mouse column or horizontal movement.</param>
/// <param name="Y">The mouse row or scroll movement.</param>
/// <param name="Button">The terminal mouse button.</param>
/// <param name="Modifiers">The terminal mouse modifiers.</param>
public sealed record DesktopCommand(string Operation, string? Target = null, string? Text = null, int Width = 0, int Height = 0,
    int X = 0, int Y = 0, int Button = 0, int Modifiers = 0);
