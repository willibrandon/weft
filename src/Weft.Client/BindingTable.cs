using Weft.Core;

namespace Weft.Client;

/// <summary>
/// The resolved direct shortcuts, including configuration overrides.
/// </summary>
internal sealed class BindingTable
{
    private readonly List<(KeyChord Chord, string Action)> _bindings = [];

    /// <summary>
    /// Gets every binding.
    /// </summary>
    internal IReadOnlyList<(KeyChord Chord, string Action)> Bindings => _bindings;

    /// <summary>
    /// Gets warnings about configuration entries that could not be applied.
    /// </summary>
    internal List<string> Warnings { get; } = [];

    /// <summary>
    /// Builds the table from configuration.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The table.</returns>
    internal static BindingTable Build(WeftConfig config)
    {
        var table = new BindingTable();
        foreach ((string action, string chord, _) in ClientActions.Defaults)
        {
            if (KeyChord.TryParse(chord, out KeyChord parsed))
            {
                table._bindings.Add((parsed, action));
            }
        }

        foreach ((string chordText, string action) in config.Bindings)
        {
            if (!KeyChord.TryParse(chordText, out KeyChord chord) || chord.Steps.Any(stroke => KeyMap.ToHex1bKey(stroke.Key) is null))
            {
                table.Warnings.Add("Ignoring binding '" + chordText + "': use one key with optional modifiers.");
                continue;
            }

            _ = table._bindings.RemoveAll(existing => existing.Chord.Equals(chord));
            if (string.Equals(action, "none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ClientActions.Defaults.Any(item => string.Equals(item.Action, action, StringComparison.Ordinal)))
            {
                table.Warnings.Add("Ignoring binding '" + chordText + "': unknown action '" + action + "'.");
                continue;
            }

            if (chord.Steps[0] is { Key: "escape", Modifiers: KeyModifiers.None })
            {
                table.Warnings.Add("Ignoring binding '" + chordText + "': Esc is reserved for the terminal and dialogs.");
                continue;
            }

            table._bindings.Add((chord, action));
        }

        return table;
    }

    /// <summary>
    /// Gets the chord text bound to an action, for display.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>The chord text, or an empty string.</returns>
    internal string ChordFor(string action)
    {
        foreach ((KeyChord chord, string bound) in _bindings)
        {
            if (string.Equals(bound, action, StringComparison.Ordinal))
            {
                return Pretty(chord.Steps[0]);
            }
        }

        return string.Empty;
    }

    private static string Pretty(KeyStroke stroke)
    {
        if (stroke.Key == "/" && stroke.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            return Prefix(stroke.Modifiers & ~KeyModifiers.Shift) + "?";
        }

        string key = stroke.Key.Length == 1 ? stroke.Key.ToUpperInvariant() : char.ToUpperInvariant(stroke.Key[0]) + stroke.Key[1..];
        return Prefix(stroke.Modifiers) + key;
    }

    private static string Prefix(KeyModifiers modifiers)
    {
        string prefix = string.Empty;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            prefix += "Ctrl+";
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            prefix += "Alt+";
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            prefix += "Shift+";
        }

        return prefix;
    }

}
