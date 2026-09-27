using Hex1b.Input;
using Weft.Core;

namespace Weft.Client;

/// <summary>
/// Maps normalized key names to Hex1b keys.
/// </summary>
internal static class KeyMap
{
    /// <summary>
    /// Maps a stroke's key name to a Hex1b key.
    /// </summary>
    /// <param name="key">The normalized key name.</param>
    /// <returns>The key, or null when the terminal input path cannot identify it.</returns>
    internal static Hex1bKey? ToHex1bKey(string key)
    {
        if (key.Length == 1)
        {
            char character = key[0];
            if (char.IsAsciiLetterLower(character))
            {
                return Hex1bKey.A + (character - 'a');
            }

            if (char.IsAsciiDigit(character))
            {
                return Hex1bKey.D0 + (character - '0');
            }

            return character switch
            {
                '-' => Hex1bKey.OemMinus,
                ',' => Hex1bKey.OemComma,
                '.' => Hex1bKey.OemPeriod,
                '/' or '?' => Hex1bKey.OemQuestion,
                '=' => Hex1bKey.OemPlus,
                _ => null
            };
        }

        return key switch
        {
            "space" => Hex1bKey.Spacebar,
            "enter" => Hex1bKey.Enter,
            "tab" => Hex1bKey.Tab,
            "escape" => Hex1bKey.Escape,
            "backspace" => Hex1bKey.Backspace,
            "delete" => Hex1bKey.Delete,
            "insert" => Hex1bKey.Insert,
            "home" => Hex1bKey.Home,
            "end" => Hex1bKey.End,
            "pageup" => Hex1bKey.PageUp,
            "pagedown" => Hex1bKey.PageDown,
            "up" => Hex1bKey.UpArrow,
            "down" => Hex1bKey.DownArrow,
            "left" => Hex1bKey.LeftArrow,
            "right" => Hex1bKey.RightArrow,
            "f1" => Hex1bKey.F1,
            "f2" => Hex1bKey.F2,
            "f3" => Hex1bKey.F3,
            "f4" => Hex1bKey.F4,
            "f5" => Hex1bKey.F5,
            "f6" => Hex1bKey.F6,
            "f7" => Hex1bKey.F7,
            "f8" => Hex1bKey.F8,
            "f9" => Hex1bKey.F9,
            "f10" => Hex1bKey.F10,
            "f11" => Hex1bKey.F11,
            "f12" => Hex1bKey.F12,
            _ => null
        };
    }

    /// <summary>
    /// Gets every stroke the terminal input path can identify, with no modifier, Shift, and Control.
    /// </summary>
    /// <returns>The strokes.</returns>
    internal static IEnumerable<KeyStroke> AllStrokes()
    {
        string[] names =
        [
            "space", "enter", "tab", "escape", "backspace", "delete", "insert", "home", "end", "pageup", "pagedown",
            "up", "down", "left", "right", "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12",
            "-", ",", ".", "/", "="
        ];
        KeyModifiers[] modifiers =
        [
            KeyModifiers.None, KeyModifiers.Shift, KeyModifiers.Control, KeyModifiers.Alt,
            KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.Alt | KeyModifiers.Shift, KeyModifiers.Control | KeyModifiers.Alt,
            KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift
        ];
        foreach (char letter in "abcdefghijklmnopqrstuvwxyz0123456789")
        {
            foreach (KeyModifiers modifier in modifiers)
            {
                yield return new KeyStroke(modifier, letter.ToString());
            }
        }

        foreach (string name in names)
        {
            foreach (KeyModifiers modifier in modifiers)
            {
                yield return new KeyStroke(modifier, name);
            }
        }
    }

    /// <summary>
    /// Converts a stroke to a Hex1b key event for sending to a block.
    /// </summary>
    /// <param name="stroke">The stroke.</param>
    /// <returns>The event, or null when the key is unknown.</returns>
    internal static Hex1bKeyEvent? ToKeyEvent(KeyStroke stroke)
    {
        if (ToHex1bKey(stroke.Key) is not { } key)
        {
            return null;
        }

        Hex1bModifiers modifiers = Hex1bModifiers.None;
        if (stroke.Modifiers.HasFlag(KeyModifiers.Control))
        {
            modifiers |= Hex1bModifiers.Control;
        }

        if (stroke.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= Hex1bModifiers.Alt;
        }

        if (stroke.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= Hex1bModifiers.Shift;
        }

        char character = stroke.Key.Length == 1 ? stroke.Key[0] : '\0';
        if (stroke.Modifiers.HasFlag(KeyModifiers.Control) && char.IsAsciiLetterLower(character))
        {
            character = (char)(character - 'a' + 1);
        }

        return new Hex1bKeyEvent(key, character, modifiers);
    }
}
