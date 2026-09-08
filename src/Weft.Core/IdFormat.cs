using System.Globalization;

namespace Weft.Core;

/// <summary>
/// Shared parsing for prefixed integer identifiers.
/// </summary>
internal static class IdFormat
{
    /// <summary>
    /// Parses a prefixed positive integer such as <c>b12</c>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="prefix">The required prefix character.</param>
    /// <param name="value">The parsed positive value.</param>
    /// <returns>Whether the text had the prefix and a positive integer.</returns>
    internal static bool TryParse(ReadOnlySpan<char> text, char prefix, out int value)
    {
        value = 0;
        if (text.Length < 2 || text[0] != prefix)
        {
            return false;
        }

        return int.TryParse(text[1..], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}
