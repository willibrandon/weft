namespace Weft.Core;

/// <summary>
/// A sequence of key strokes that triggers an action, parsed from text such as <c>leader h</c> or <c>alt+enter</c>.
/// </summary>
/// <param name="Steps">The strokes in order.</param>
public sealed record KeyChord(IReadOnlyList<KeyStroke> Steps)
{
    private static readonly Dictionary<string, string> s_aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "space",
        ["spacebar"] = "space",
        ["esc"] = "escape",
        ["escape"] = "escape",
        ["return"] = "enter",
        ["ret"] = "enter",
        ["enter"] = "enter",
        ["tab"] = "tab",
        ["bs"] = "backspace",
        ["backspace"] = "backspace",
        ["del"] = "delete",
        ["delete"] = "delete",
        ["ins"] = "insert",
        ["insert"] = "insert",
        ["home"] = "home",
        ["end"] = "end",
        ["pgup"] = "pageup",
        ["pageup"] = "pageup",
        ["pgdn"] = "pagedown",
        ["pagedown"] = "pagedown",
        ["up"] = "up",
        ["down"] = "down",
        ["left"] = "left",
        ["right"] = "right",
        ["minus"] = "-",
        ["dash"] = "-",
        ["comma"] = ",",
        ["period"] = ".",
        ["dot"] = ".",
        ["slash"] = "/",
        ["question"] = "?",
        ["equals"] = "=",
        ["plus"] = "="
    };

    /// <summary>
    /// Parses chord text, expanding the token <c>leader</c> to the leader chord.
    /// </summary>
    /// <param name="text">The chord text, steps separated by spaces, modifiers joined with <c>+</c>.</param>
    /// <param name="leader">The leader chord, or null when <c>leader</c> is not allowed.</param>
    /// <param name="chord">The parsed chord.</param>
    /// <returns>Whether the text was valid.</returns>
    public static bool TryParse(string text, KeyChord? leader, out KeyChord chord)
    {
        chord = new KeyChord([]);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        List<KeyStroke> steps = [];
        foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(token, "leader", StringComparison.OrdinalIgnoreCase))
            {
                if (leader is null)
                {
                    return false;
                }

                steps.AddRange(leader.Steps);
                continue;
            }

            if (!TryParseStroke(token, out KeyStroke stroke))
            {
                return false;
            }

            steps.Add(stroke);
        }

        if (steps.Count == 0)
        {
            return false;
        }

        chord = new KeyChord(steps);
        return true;
    }

    /// <summary>
    /// Parses one stroke such as <c>ctrl+shift+x</c>, <c>H</c>, or <c>f5</c>.
    /// </summary>
    /// <param name="token">The stroke text.</param>
    /// <param name="stroke">The parsed stroke.</param>
    /// <returns>Whether the token was valid.</returns>
    public static bool TryParseStroke(string token, out KeyStroke stroke)
    {
        stroke = default;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        KeyModifiers modifiers = KeyModifiers.None;
        string key = token;
        while (true)
        {
            int plus = key.IndexOf('+', StringComparison.Ordinal);
            if (plus <= 0 || plus == key.Length - 1)
            {
                break;
            }

            string modifier = key[..plus];
            key = key[(plus + 1)..];
            switch (modifier.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                case "C":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "ALT":
                case "META":
                case "M":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "SHIFT":
                case "S":
                    modifiers |= KeyModifiers.Shift;
                    break;
                default:
                    return false;
            }
        }

        if (key.Length == 1)
        {
            char character = key[0];
            if (char.IsAsciiLetterUpper(character))
            {
                modifiers |= KeyModifiers.Shift;
                key = char.ToLowerInvariant(character).ToString();
            }
            else if (character == '?')
            {
                // Terminals report ? as the slash key with Shift, so the chord matches what arrives.
                modifiers |= KeyModifiers.Shift;
                key = "/";
            }
            else if (char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or ',' or '.' or '/' or '=')
            {
                key = character.ToString();
            }
            else
            {
                return false;
            }
        }
        else if (s_aliases.TryGetValue(key, out string? alias))
        {
            key = alias;
        }
        else if (key.Length is 2 or 3 && (key[0] is 'f' or 'F') && int.TryParse(key.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int function) && function is >= 1 and <= 12)
        {
            key = "f" + function.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            return false;
        }

        stroke = new KeyStroke(modifiers, key);
        return true;
    }

    /// <summary>
    /// Formats the chord as configuration text.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => string.Join(' ', Steps);

    /// <summary>
    /// Compares chords by their strokes, since two parses of the same text produce different lists.
    /// </summary>
    /// <param name="other">The other chord.</param>
    /// <returns>True when both chords have the same strokes in the same order.</returns>
    public bool Equals(KeyChord? other) => other is not null && Steps.SequenceEqual(other.Steps);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (KeyStroke stroke in Steps)
        {
            hash.Add(stroke);
        }

        return hash.ToHashCode();
    }
}
