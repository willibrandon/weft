namespace Weft.Client;

/// <summary>
/// One row of the command palette.
/// </summary>
/// <param name="Action">The action id.</param>
/// <param name="Description">The description.</param>
/// <param name="Chord">The bound chord text, or empty.</param>
internal sealed record PaletteEntry(string Action, string Description, string Chord)
{
    /// <summary>
    /// Formats the row for display.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => Description.PadRight(34) + "  " + Chord;
}
