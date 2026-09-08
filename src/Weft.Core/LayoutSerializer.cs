using System.Globalization;
using System.Text;

namespace Weft.Core;

/// <summary>
/// Converts layout trees to and from tmux's checksummed layout string grammar.
/// </summary>
public static class LayoutSerializer
{
    /// <summary>
    /// Serializes a tree to <c>csum,WxH,X,Y[,block]</c> with <c>{}</c> for left-right splits and <c>[]</c> for top-bottom splits.
    /// </summary>
    /// <param name="root">The root cell.</param>
    /// <returns>The layout string.</returns>
    public static string Serialize(LayoutCell root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var body = new StringBuilder();
        Append(root, body);
        string text = body.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"{Checksum(text):x4},{text}");
    }

    /// <summary>
    /// Parses a layout string, verifying its checksum.
    /// </summary>
    /// <param name="text">The layout string.</param>
    /// <param name="root">The parsed root, whose leaves carry block ids when the string had them.</param>
    /// <returns>Whether the string was well formed with a matching checksum.</returns>
    public static bool TryParse(string text, out LayoutCell? root)
    {
        root = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int comma = text.IndexOf(',', StringComparison.Ordinal);
        if (comma != 4 || !ushort.TryParse(text.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort expected))
        {
            return false;
        }

        string body = text[(comma + 1)..];
        if (Checksum(body) != expected)
        {
            return false;
        }

        int position = 0;
        LayoutCell? parsed = ParseCell(body, ref position);
        if (parsed is null || position != body.Length)
        {
            return false;
        }

        root = parsed;
        return true;
    }

    private static ushort Checksum(string body)
    {
        ushort checksum = 0;
        foreach (char character in body)
        {
            checksum = (ushort)((checksum >> 1) + ((checksum & 1) << 15));
            checksum = (ushort)(checksum + character);
        }

        return checksum;
    }

    private static void Append(LayoutCell cell, StringBuilder body)
    {
        body.Append(CultureInfo.InvariantCulture, $"{cell.Width}x{cell.Height},{cell.X},{cell.Y}");
        if (cell.IsLeaf)
        {
            if (cell.Block is { } block)
            {
                body.Append(CultureInfo.InvariantCulture, $",{block.Value}");
            }

            return;
        }

        body.Append(cell.Orientation == SplitOrientation.LeftRight ? '{' : '[');
        for (int i = 0; i < cell.Children.Count; i++)
        {
            if (i > 0)
            {
                body.Append(',');
            }

            Append(cell.Children[i], body);
        }

        body.Append(cell.Orientation == SplitOrientation.LeftRight ? '}' : ']');
    }

    private static LayoutCell? ParseCell(string body, ref int position)
    {
        if (!ReadNumber(body, ref position, out int width) || !Expect(body, ref position, 'x')
            || !ReadNumber(body, ref position, out int height) || !Expect(body, ref position, ',')
            || !ReadNumber(body, ref position, out int x) || !Expect(body, ref position, ',')
            || !ReadNumber(body, ref position, out int y))
        {
            return null;
        }

        LayoutCell cell;
        if (position < body.Length && body[position] is '{' or '[')
        {
            char open = body[position];
            char close = open == '{' ? '}' : ']';
            position++;
            cell = LayoutCell.CreateSplit(open == '{' ? SplitOrientation.LeftRight : SplitOrientation.TopBottom);
            while (true)
            {
                LayoutCell? child = ParseCell(body, ref position);
                if (child is null)
                {
                    return null;
                }

                cell.AddChild(child);
                if (position < body.Length && body[position] == ',')
                {
                    position++;
                    continue;
                }

                break;
            }

            if (!Expect(body, ref position, close) || cell.Children.Count < 2)
            {
                return null;
            }
        }
        else
        {
            BlockId? block = null;
            if (position < body.Length && body[position] == ',')
            {
                int save = position;
                position++;
                if (ReadNumber(body, ref position, out int id) && id > 0)
                {
                    block = new BlockId(id);
                }
                else
                {
                    position = save;
                }
            }

            cell = LayoutCell.CreateLeaf(block ?? new BlockId(1));
            cell.Block = block;
        }

        cell.X = x;
        cell.Y = y;
        cell.Width = width;
        cell.Height = height;
        return cell;
    }

    private static bool Expect(string body, ref int position, char expected)
    {
        if (position < body.Length && body[position] == expected)
        {
            position++;
            return true;
        }

        return false;
    }

    private static bool ReadNumber(string body, ref int position, out int value)
    {
        int start = position;
        while (position < body.Length && char.IsAsciiDigit(body[position]))
        {
            position++;
        }

        return int.TryParse(body.AsSpan(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
