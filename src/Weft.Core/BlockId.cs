using System.Globalization;

namespace Weft.Core;

/// <summary>
/// Identifies a block for the lifetime of a server; values are never reused.
/// </summary>
/// <param name="Value">The positive integer value.</param>
public readonly record struct BlockId(int Value)
{
    /// <summary>
    /// The prefix character that distinguishes block ids in text form.
    /// </summary>
    public const char Prefix = 'b';

    /// <summary>
    /// Parses the text form of a block id such as <c>b12</c>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="id">The parsed id when the text is valid.</param>
    /// <returns>Whether the text was a valid block id.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out BlockId id)
    {
        if (IdFormat.TryParse(text, Prefix, out int value))
        {
            id = new BlockId(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>
    /// Formats the id in its text form such as <c>b12</c>.
    /// </summary>
    /// <returns>The text form.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{Value}");
}
