using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Weft.Core;

/// <summary>
/// Encodes tmux-style key names such as <c>Enter</c>, <c>C-c</c>, <c>M-x</c>, or <c>Up</c> into terminal input bytes.
/// </summary>
public static class KeyEncoder
{
    private const byte Escape = 0x1b;

    /// <summary>
    /// Encodes one key name or literal text.
    /// </summary>
    /// <param name="key">The key name or text.</param>
    /// <param name="applicationCursorKeys">Whether the terminal has application cursor keys enabled.</param>
    /// <returns>The bytes to write to the pseudo-terminal.</returns>
    public static byte[] Encode(string key, bool applicationCursorKeys)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (TryEncodeNamed(key, applicationCursorKeys, out byte[]? bytes))
        {
            return bytes;
        }

        return Encoding.UTF8.GetBytes(key);
    }

    /// <summary>
    /// Encodes literal text with no key-name interpretation.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The UTF-8 bytes.</returns>
    public static byte[] EncodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Wraps text in bracketed-paste markers.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes including the begin and end markers.</returns>
    public static byte[] EncodeBracketedPaste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] begin = "\x1b[200~"u8.ToArray();
        byte[] end = "\x1b[201~"u8.ToArray();
        byte[] result = new byte[begin.Length + payload.Length + end.Length];
        begin.CopyTo(result, 0);
        payload.CopyTo(result, begin.Length);
        end.CopyTo(result, begin.Length + payload.Length);
        return result;
    }

    /// <summary>
    /// Tries to encode a named key, including modifier prefixes.
    /// </summary>
    /// <param name="key">The key name.</param>
    /// <param name="applicationCursorKeys">Whether application cursor keys are enabled.</param>
    /// <param name="bytes">The encoded bytes when the name is known.</param>
    /// <returns>Whether the name was recognised.</returns>
    public static bool TryEncodeNamed(string key, bool applicationCursorKeys, [NotNullWhen(true)] out byte[]? bytes)
    {
        ArgumentNullException.ThrowIfNull(key);
        bytes = null;
        bool control = false;
        bool meta = false;
        bool shift = false;
        string rest = key;
        while (rest.Length > 2 && rest[1] == '-')
        {
            char modifier = char.ToUpperInvariant(rest[0]);
            if (modifier == 'C')
            {
                control = true;
            }
            else if (modifier == 'M')
            {
                meta = true;
            }
            else if (modifier == 'S')
            {
                shift = true;
            }
            else
            {
                break;
            }

            rest = rest[2..];
        }

        if (rest.Length == 0)
        {
            return false;
        }

        byte[] body;
        if (TryEncodeSpecial(rest, applicationCursorKeys, control, shift, out byte[]? special))
        {
            body = special;
        }
        else if (rest.Length == 1)
        {
            char character = rest[0];
            if (control)
            {
                if (!TryControl(character, out byte controlByte))
                {
                    return false;
                }

                body = [controlByte];
            }
            else if (!meta && !shift)
            {
                return false;
            }
            else
            {
                body = Encoding.UTF8.GetBytes(shift ? char.ToUpperInvariant(character).ToString() : rest);
            }
        }
        else
        {
            return false;
        }

        bytes = meta ? [Escape, .. body] : body;
        return true;
    }

    private static bool TryControl(char character, out byte controlByte)
    {
        char upper = char.ToUpperInvariant(character);
        if (upper is >= 'A' and <= 'Z')
        {
            controlByte = (byte)(upper - 'A' + 1);
            return true;
        }

        switch (character)
        {
            case ' ':
            case '@':
                controlByte = 0;
                return true;
            case '[':
                controlByte = 0x1b;
                return true;
            case '\\':
                controlByte = 0x1c;
                return true;
            case ']':
                controlByte = 0x1d;
                return true;
            case '^':
                controlByte = 0x1e;
                return true;
            case '_':
                controlByte = 0x1f;
                return true;
            case '?':
                controlByte = 0x7f;
                return true;
            default:
                controlByte = 0;
                return false;
        }
    }

    private static bool TryEncodeSpecial(string name, bool applicationCursorKeys, bool control, bool shift, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        string? cursor = name.ToUpperInvariant() switch
        {
            "UP" => "A",
            "DOWN" => "B",
            "RIGHT" => "C",
            "LEFT" => "D",
            "HOME" => "H",
            "END" => "F",
            _ => null
        };
        if (cursor is not null)
        {
            int modifier = 1 + (shift ? 1 : 0) + (control ? 4 : 0);
            bytes = modifier > 1
                ? Encoding.ASCII.GetBytes("\x1b[1;" + modifier.ToString(System.Globalization.CultureInfo.InvariantCulture) + cursor)
                : Encoding.ASCII.GetBytes((applicationCursorKeys ? "\x1bO" : "\x1b[") + cursor);
            return true;
        }

        string? tilde = name.ToUpperInvariant() switch
        {
            "INSERT" or "IC" => "2",
            "DELETE" or "DC" => "3",
            "PAGEUP" or "PPAGE" or "PGUP" => "5",
            "PAGEDOWN" or "NPAGE" or "PGDN" => "6",
            "F5" => "15",
            "F6" => "17",
            "F7" => "18",
            "F8" => "19",
            "F9" => "20",
            "F10" => "21",
            "F11" => "23",
            "F12" => "24",
            _ => null
        };
        if (tilde is not null)
        {
            int modifier = 1 + (shift ? 1 : 0) + (control ? 4 : 0);
            bytes = modifier > 1
                ? Encoding.ASCII.GetBytes("\x1b[" + tilde + ";" + modifier.ToString(System.Globalization.CultureInfo.InvariantCulture) + "~")
                : Encoding.ASCII.GetBytes("\x1b[" + tilde + "~");
            return true;
        }

        bytes = name.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => [(byte)'\r'],
            "TAB" when shift => "\x1b[Z"u8.ToArray(),
            "TAB" => [(byte)'\t'],
            "BTAB" => "\x1b[Z"u8.ToArray(),
            "ESCAPE" or "ESC" => [Escape],
            "SPACE" => [(byte)' '],
            "BSPACE" or "BACKSPACE" => [0x7f],
            "F1" => "\x1bOP"u8.ToArray(),
            "F2" => "\x1bOQ"u8.ToArray(),
            "F3" => "\x1bOR"u8.ToArray(),
            "F4" => "\x1bOS"u8.ToArray(),
            _ => null
        };
        return bytes is not null;
    }
}
