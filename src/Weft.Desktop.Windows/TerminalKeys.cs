using System.Globalization;
using Windows.System;

namespace Weft.Desktop.Windows;

/// <summary>
/// Translates Windows key presses into the named keys the server encodes for the terminal.
/// </summary>
/// <remarks>
/// Printable text, dead keys, AltGr characters, and input-method results arrive through the input
/// control's text instead, so they follow the active keyboard layout exactly.
/// </remarks>
internal static class TerminalKeys
{
    private static readonly Dictionary<VirtualKey, string> s_special = new()
    {
        [VirtualKey.Enter] = "Enter",
        [VirtualKey.Tab] = "Tab",
        [VirtualKey.Back] = "BSpace",
        [VirtualKey.Escape] = "Escape",
        [VirtualKey.Left] = "Left",
        [VirtualKey.Right] = "Right",
        [VirtualKey.Up] = "Up",
        [VirtualKey.Down] = "Down",
        [VirtualKey.Home] = "Home",
        [VirtualKey.End] = "End",
        [VirtualKey.PageUp] = "PageUp",
        [VirtualKey.PageDown] = "PageDown",
        [VirtualKey.Insert] = "Insert",
        [VirtualKey.Delete] = "Delete"
    };

    // Control characters for the ASCII punctuation terminals expect, keyed by US key positions.
    private static readonly Dictionary<VirtualKey, char> s_control = new()
    {
        [VirtualKey.Space] = '@',
        [VirtualKey.Number2] = '@',
        [(VirtualKey)0xBF] = '_',
        [(VirtualKey)0xDB] = '[',
        [(VirtualKey)0xDC] = '\\',
        [(VirtualKey)0xDD] = ']'
    };

    /// <summary>
    /// Gets the named key for a press, or null when the press produces text or nothing.
    /// </summary>
    /// <param name="key">The virtual key.</param>
    /// <param name="modifiers">The Ctrl, Alt, and Shift state.</param>
    /// <param name="altGraph">Whether AltGr, reported as Ctrl with the right Alt key, is held.</param>
    /// <returns>A name such as <c>Enter</c>, <c>C-Left</c>, or <c>C-c</c>, or null.</returns>
    internal static string? Name(VirtualKey key, VirtualKeyModifiers modifiers, bool altGraph)
    {
        if (altGraph)
        {
            return null;
        }

        bool control = modifiers.HasFlag(VirtualKeyModifiers.Control);
        bool alt = modifiers.HasFlag(VirtualKeyModifiers.Menu);
        bool shift = modifiers.HasFlag(VirtualKeyModifiers.Shift);
        string prefix = (control ? "C-" : string.Empty) + (alt ? "M-" : string.Empty);
        if (Special(key) is { } special)
        {
            return prefix + (shift ? "S-" : string.Empty) + special;
        }

        char? character = null;
        if (control)
        {
            character = key is >= VirtualKey.A and <= VirtualKey.Z ? (char)('a' + (key - VirtualKey.A))
                : key == VirtualKey.Number6 && shift ? '^'
                : key == (VirtualKey)0xBD && shift ? '_'
                : s_control.TryGetValue(key, out char punctuation) ? punctuation
                : null;
        }
        else if (alt)
        {
            character = key is >= VirtualKey.A and <= VirtualKey.Z ? (char)((shift ? 'A' : 'a') + (key - VirtualKey.A))
                : key is >= VirtualKey.Number0 and <= VirtualKey.Number9 ? (char)('0' + (key - VirtualKey.Number0))
                : null;
        }

        return character is { } value ? prefix + value : null;
    }

    private static string? Special(VirtualKey key)
    {
        return key is >= VirtualKey.F1 and <= VirtualKey.F12
            ? "F" + (key - VirtualKey.F1 + 1).ToString(CultureInfo.InvariantCulture)
            : s_special.GetValueOrDefault(key);
    }
}
