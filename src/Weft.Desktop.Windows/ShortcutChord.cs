using System.Globalization;
using Windows.System;

namespace Weft.Desktop.Windows;

/// <summary>
/// A key and its modifiers, stored as text such as <c>ctrl+shift+t</c> and shown as <c>Ctrl+Shift+T</c>.
/// </summary>
/// <param name="Key">The virtual key.</param>
/// <param name="Modifiers">The Ctrl, Alt, and Shift modifiers.</param>
internal readonly record struct ShortcutChord(VirtualKey Key, VirtualKeyModifiers Modifiers)
{
    private static readonly (string Name, VirtualKey Key, string Display)[] s_named =
    [
        ("tab", VirtualKey.Tab, "Tab"),
        ("enter", VirtualKey.Enter, "Enter"),
        ("space", VirtualKey.Space, "Space"),
        ("pageup", VirtualKey.PageUp, "PgUp"),
        ("pagedown", VirtualKey.PageDown, "PgDn"),
        ("home", VirtualKey.Home, "Home"),
        ("end", VirtualKey.End, "End"),
        ("insert", VirtualKey.Insert, "Ins"),
        ("delete", VirtualKey.Delete, "Del"),
        ("left", VirtualKey.Left, "Left"),
        ("right", VirtualKey.Right, "Right"),
        ("up", VirtualKey.Up, "Up"),
        ("down", VirtualKey.Down, "Down"),
        ("plus", (VirtualKey)0xBB, "Plus"),
        ("comma", (VirtualKey)0xBC, ","),
        ("minus", (VirtualKey)0xBD, "Minus"),
        ("period", (VirtualKey)0xBE, "."),
        ("slash", (VirtualKey)0xBF, "/"),
        ("semicolon", (VirtualKey)0xBA, ";"),
        ("backtick", (VirtualKey)0xC0, "`"),
        ("[", (VirtualKey)0xDB, "["),
        ("backslash", (VirtualKey)0xDC, "\\"),
        ("]", (VirtualKey)0xDD, "]"),
        ("quote", (VirtualKey)0xDE, "'")
    ];

    private static readonly Dictionary<string, VirtualKeyModifiers> s_modifiers = new(StringComparer.Ordinal)
    {
        ["ctrl"] = VirtualKeyModifiers.Control,
        ["alt"] = VirtualKeyModifiers.Menu,
        ["shift"] = VirtualKeyModifiers.Shift
    };

    /// <summary>
    /// Parses a stored chord; unknown keys or modifiers yield null.
    /// </summary>
    /// <param name="text">Text such as <c>alt+shift+plus</c>.</param>
    /// <returns>The chord, or null.</returns>
    internal static ShortcutChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.ToLowerInvariant().Split('+');
        VirtualKeyModifiers modifiers = VirtualKeyModifiers.None;
        foreach (string part in parts[..^1])
        {
            if (!s_modifiers.TryGetValue(part, out VirtualKeyModifiers modifier))
            {
                return null;
            }

            modifiers |= modifier;
        }

        return KeyFromName(parts[^1]) is { } key ? new ShortcutChord(key, modifiers) : null;
    }

    /// <summary>
    /// Maps the numeric keypad's Plus and Minus to their main-keyboard equivalents.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    /// <returns>The key used for matching.</returns>
    internal static VirtualKey Normalize(VirtualKey key)
    {
        return key == VirtualKey.Add ? (VirtualKey)0xBB : key == VirtualKey.Subtract ? (VirtualKey)0xBD : key;
    }

    /// <summary>
    /// Gets whether the key can be named in a shortcut.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether the key has a stored name.</returns>
    internal static bool IsNameable(VirtualKey key)
    {
        return NameOf(Normalize(key)) is not null;
    }

    /// <summary>
    /// Gets the stored form, such as <c>ctrl+shift+t</c>.
    /// </summary>
    /// <returns>The chord text.</returns>
    public override string ToString()
    {
        return Prefix("ctrl+", "alt+", "shift+") + NameOf(Key);
    }

    /// <summary>
    /// Gets the form shown beside menu items and commands, such as <c>Ctrl+Shift+T</c>.
    /// </summary>
    /// <returns>The display text.</returns>
    internal string Display()
    {
        VirtualKey key = Key;
        string? named = s_named.FirstOrDefault(entry => entry.Key == key).Display;
        return Prefix("Ctrl+", "Alt+", "Shift+") + (named ?? NameOf(key)?.ToUpperInvariant() ?? string.Empty);
    }

    private string Prefix(string control, string alt, string shift)
    {
        return (Modifiers.HasFlag(VirtualKeyModifiers.Control) ? control : string.Empty)
            + (Modifiers.HasFlag(VirtualKeyModifiers.Menu) ? alt : string.Empty)
            + (Modifiers.HasFlag(VirtualKeyModifiers.Shift) ? shift : string.Empty);
    }

    private static string? NameOf(VirtualKey key)
    {
        return key is >= VirtualKey.A and <= VirtualKey.Z ? ((char)('a' + (key - VirtualKey.A))).ToString()
            : key is >= VirtualKey.Number0 and <= VirtualKey.Number9
            ? ((char)('0' + (key - VirtualKey.Number0))).ToString()
            : key is >= VirtualKey.F1 and <= VirtualKey.F24
            ? "f" + (key - VirtualKey.F1 + 1).ToString(CultureInfo.InvariantCulture)
            : s_named.FirstOrDefault(entry => entry.Key == key).Name;
    }

    private static VirtualKey? KeyFromName(string name)
    {
        if (name.Length == 1 && name[0] is >= 'a' and <= 'z')
        {
            return VirtualKey.A + (name[0] - 'a');
        }

        if (name.Length == 1 && name[0] is >= '0' and <= '9')
        {
            return VirtualKey.Number0 + (name[0] - '0');
        }

        int index = Array.FindIndex(s_named, entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
        return name.Length > 1 && name[0] == 'f'
            && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int function)
            && function is >= 1 and <= 24
            ? VirtualKey.F1 + (function - 1)
            : index < 0 ? null : s_named[index].Key;
    }
}
