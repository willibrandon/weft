using System.Text;

namespace Weft.Server;

/// <summary>
/// Keeps the first and last part of a text within a byte budget, marking what was omitted.
/// </summary>
internal static class HeadTailBuffer
{
    /// <summary>
    /// Truncates text to a byte budget split evenly between head and tail.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="budget">The maximum UTF-8 bytes to keep.</param>
    /// <returns>The kept text, whether truncation happened, the total bytes, and the omitted bytes.</returns>
    internal static (string Text, bool Truncated, long TotalBytes, long OmittedBytes) Truncate(string text, int budget)
    {
        int total = Encoding.UTF8.GetByteCount(text);
        if (total <= budget || budget <= 0)
        {
            return (text, false, total, 0);
        }

        int headBytes = budget / 2;
        int tailBytes = budget - headBytes;
        int headChars = CharsWithinBytes(text, 0, headBytes, forward: true);
        int tailChars = CharsWithinBytes(text, text.Length, tailBytes, forward: false);
        if (headChars + tailChars >= text.Length)
        {
            return (text, false, total, 0);
        }

        string head = text[..headChars];
        string tail = text[^tailChars..];
        long omitted = total - Encoding.UTF8.GetByteCount(head) - Encoding.UTF8.GetByteCount(tail);
        string marker = "\n[... " + omitted.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes omitted ...]\n";
        return (head + marker + tail, true, total, omitted);
    }

    private static int CharsWithinBytes(string text, int start, int budget, bool forward)
    {
        int bytes = 0;
        int chars = 0;
        int index = start;
        while (forward ? index < text.Length : index > 0)
        {
            int at = forward ? index : index - 1;
            int length = 1;
            if (char.IsHighSurrogate(text[at]) && forward && at + 1 < text.Length)
            {
                length = 2;
            }
            else if (char.IsLowSurrogate(text[at]) && !forward && at > 0)
            {
                length = 2;
                at--;
            }

            int size = Encoding.UTF8.GetByteCount(text.AsSpan(at, length));
            if (bytes + size > budget)
            {
                break;
            }

            bytes += size;
            chars += length;
            index = forward ? index + length : index - length;
        }

        return chars;
    }
}
