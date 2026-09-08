using System.Globalization;

namespace Weft.Core;

/// <summary>
/// Identifies a session for the lifetime of a server; values are never reused.
/// </summary>
/// <param name="Value">The positive integer value.</param>
public readonly record struct SessionId(int Value)
{
    /// <summary>
    /// The prefix character that distinguishes session ids in text form.
    /// </summary>
    public const char Prefix = 's';

    /// <summary>
    /// Parses the text form of a session id such as <c>s3</c>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="id">The parsed id when the text is valid.</param>
    /// <returns>Whether the text was a valid session id.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out SessionId id)
    {
        if (IdFormat.TryParse(text, Prefix, out int value))
        {
            id = new SessionId(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>
    /// Formats the id in its text form such as <c>s3</c>.
    /// </summary>
    /// <returns>The text form.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{Value}");
}
