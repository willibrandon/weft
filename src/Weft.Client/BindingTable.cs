using Weft.Core;

namespace Weft.Client;

/// <summary>
/// The resolved chord to action map: defaults, aliases, and configuration overrides.
/// </summary>
internal sealed class BindingTable
{
    private readonly List<(KeyChord Chord, string Action)> _bindings = [];

    private BindingTable(KeyChord leader)
    {
        Leader = leader;
    }

    /// <summary>
    /// Gets the leader chord.
    /// </summary>
    internal KeyChord Leader { get; }

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
        if (!KeyChord.TryParse(config.Leader, null, out KeyChord leader))
        {
            KeyChord.TryParse("ctrl+b", null, out leader);
        }

        var table = new BindingTable(leader);
        foreach ((string action, string chord, _) in ClientActions.Defaults)
        {
            if (string.Equals(action, ClientActions.SendLeader, StringComparison.Ordinal))
            {
                // Sending the leader is always the leader pressed twice, whatever the leader is configured to be.
                table._bindings.Add((new KeyChord([.. leader.Steps, .. leader.Steps]), action));
                continue;
            }

            table.Add(chord, action);
        }

        foreach ((string action, string chord) in ClientActions.Aliases)
        {
            table.Add(chord, action);
        }

        foreach ((string chordText, string action) in config.Bindings)
        {
            if (!KeyChord.TryParse(chordText, leader, out KeyChord chord))
            {
                table.Warnings.Add("Ignoring binding '" + chordText + "': unknown chord.");
                continue;
            }

            table._bindings.RemoveAll(existing => existing.Chord.Equals(chord));
            if (string.Equals(action, "none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ClientActions.Defaults.Any(item => string.Equals(item.Action, action, StringComparison.Ordinal)))
            {
                table.Warnings.Add("Ignoring binding '" + chordText + "': unknown action '" + action + "'.");
                continue;
            }

            if (!table.IsUsable(chord))
            {
                // A chord the client could never register must not count as bound, or lock mode would strand it.
                table.Warnings.Add("Ignoring binding '" + chordText + "': a single-stroke leader takes exactly one key after it.");
                continue;
            }

            table._bindings.Add((chord, action));
        }

        return table;
    }

    // A single-stroke leader arms exactly one key after it, so a longer remainder can never be entered,
    // and a stroke without a terminal key cannot be matched at all.
    private bool IsUsable(KeyChord chord) =>
        chord.Steps.All(stroke => KeyMap.ToHex1bKey(stroke.Key) is not null) &&
        (Leader.Steps.Count != 1 || !TryStripLeader(chord, out IReadOnlyList<KeyStroke> rest) || rest.Count == 1);

    /// <summary>
    /// Splits a chord into the leader prefix and the strokes that follow it.
    /// </summary>
    /// <param name="chord">The chord.</param>
    /// <param name="rest">The strokes after the leader, when the chord starts with it.</param>
    /// <returns>Whether the chord starts with the leader.</returns>
    internal bool TryStripLeader(KeyChord chord, out IReadOnlyList<KeyStroke> rest)
    {
        if (chord.Steps.Count > Leader.Steps.Count && chord.Steps.Take(Leader.Steps.Count).SequenceEqual(Leader.Steps))
        {
            rest = [.. chord.Steps.Skip(Leader.Steps.Count)];
            return true;
        }

        rest = [];
        return false;
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
                return Describe(chord);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Formats a chord for people, collapsing the leader prefix.
    /// </summary>
    /// <param name="chord">The chord.</param>
    /// <returns>The text.</returns>
    internal string Describe(KeyChord chord)
    {
        List<string> parts = [];
        int index = 0;
        if (chord.Steps.Count > Leader.Steps.Count && chord.Steps.Take(Leader.Steps.Count).SequenceEqual(Leader.Steps))
        {
            parts.Add(string.Join(' ', Leader.Steps.Select(Pretty)));
            index = Leader.Steps.Count;
        }

        for (; index < chord.Steps.Count; index++)
        {
            parts.Add(Pretty(chord.Steps[index]));
        }

        return string.Join(' ', parts);
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

    private void Add(string chordText, string action)
    {
        if (KeyChord.TryParse(chordText, Leader, out KeyChord chord))
        {
            _bindings.Add((chord, action));
        }
    }
}
