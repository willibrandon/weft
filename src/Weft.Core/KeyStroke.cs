namespace Weft.Core;

/// <summary>
/// One key with modifiers, as one step of a chord.
/// </summary>
/// <param name="Modifiers">The modifiers.</param>
/// <param name="Key">The normalized key name: a lower-case letter, a digit, a symbol, or a name such as <c>enter</c>.</param>
public readonly record struct KeyStroke(KeyModifiers Modifiers, string Key)
{
    /// <summary>
    /// Formats the stroke as configuration text such as <c>ctrl+b</c>.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        string prefix = string.Empty;
        if (Modifiers.HasFlag(KeyModifiers.Control))
        {
            prefix += "ctrl+";
        }

        if (Modifiers.HasFlag(KeyModifiers.Alt))
        {
            prefix += "alt+";
        }

        if (Modifiers.HasFlag(KeyModifiers.Shift))
        {
            prefix += "shift+";
        }

        return prefix + Key;
    }
}
