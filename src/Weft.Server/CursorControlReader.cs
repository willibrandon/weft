using Hex1b.Tokens;
using System.Text;

namespace Weft.Server;

/// <summary>
/// Finds cursor-state control boundaries without changing terminal bytes or retaining graphic payloads.
/// </summary>
internal sealed class CursorControlReader
{
    private const int Ground = 0;
    private const int Escape = 1;
    private const int Csi = 2;
    private const int String = 3;
    private const int StringEscape = 4;
    private const int Intermediate = 5;
    private const int MaximumHeader = 1024;
    private readonly StringBuilder _header = new();
    private int _state;
    private int _utf8Remaining;
    private bool _utf8Control;
    private bool _osc;
    private bool _overflow;

    /// <summary>
    /// Gets the cursor controls ending the most recently inspected batch.
    /// </summary>
    internal IReadOnlyList<AnsiToken> Controls { get; private set; } = [];

    /// <summary>
    /// Gets whether an oversized control made cursor mode tracking uncertain.
    /// </summary>
    internal bool Uncertain { get; private set; }

    /// <summary>
    /// Returns a byte prefix ending at a relevant control, or the entire supplied span.
    /// </summary>
    /// <param name="bytes">Unmodified bytes about to reach the terminal.</param>
    /// <returns>The length that can be applied before observing cursor state.</returns>
    internal int Read(ReadOnlySpan<byte> bytes)
    {
        Controls = [];
        Uncertain = false;
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            if (_utf8Remaining > 0 && value is >= 0x80 and <= 0xbf)
            {
                _utf8Remaining--;
                if (!_utf8Control || value is not (>= 0x80 and <= 0x9f))
                {
                    continue;
                }
            }
            _utf8Control = value == 0xc2;
            _utf8Remaining = value switch
            {
                >= 0xc2 and <= 0xdf => 1,
                >= 0xe0 and <= 0xef => 2,
                >= 0xf0 and <= 0xf4 => 3,
                _ => 0
            };

            if (value is 0x18 or 0x1a)
            {
                _state = Ground;
                _ = _header.Clear();
                continue;
            }
            if (_state is String or StringEscape)
            {
                if (value == 0x9c || (_osc && value == 7) || (_state == StringEscape && value == (byte)'\\'))
                {
                    _state = Ground;
                    continue;
                }
                _state = value == 0x1b ? StringEscape : String;
                continue;
            }
            if (value == 0x1b)
            {
                _ = _header.Clear().Append('\x1b');
                _overflow = false;
                _state = Escape;
                continue;
            }
            if (_state == Ground)
            {
                if (value == 0x9b)
                {
                    // Conservatively stop projecting until modes are known again.
                    // The observer does not interpret eight-bit CSI forms itself.
                    _ = _header.Clear();
                    _overflow = true;
                    _state = Csi;
                }
                else if (value is 0x90 or 0x98 or 0x9d or 0x9e or 0x9f)
                {
                    _osc = value == 0x9d;
                    _state = String;
                }
                continue;
            }

            if (_header.Length < MaximumHeader)
            {
                _ = _header.Append((char)value);
            }
            else
            {
                _overflow = true;
            }
            if (_state == Escape)
            {
                if (value is (byte)']' or (byte)'P' or (byte)'_' or (byte)'^' or (byte)'X')
                {
                    _osc = value == (byte)']';
                    _state = String;
                    _ = _header.Clear();
                    continue;
                }
                if (value == (byte)'[')
                {
                    _state = Csi;
                    continue;
                }
                if (value is >= 0x20 and <= 0x2f)
                {
                    _state = Intermediate;
                    continue;
                }
            }
            else if ((_state == Csi && value is not (>= 0x40 and <= 0x7e))
                || (_state == Intermediate && value is not (>= 0x30 and <= 0x7e)))
            {
                continue;
            }

            _state = Ground;
            if (_overflow)
            {
                Uncertain = true;
                _ = _header.Clear();
                return index + 1;
            }
            if (value is (byte)'7' or (byte)'8' or (byte)'s' or (byte)'u' or (byte)'h' or (byte)'l' or (byte)'p' or (byte)'c')
            {
                IReadOnlyList<AnsiToken> tokens = AnsiTokenizer.Tokenize(_header.ToString());
                if (tokens.Any(token => token is SaveCursorToken or RestoreCursorToken or SoftResetToken or RisToken
                    or PrivateModeToken { Mode: 6 or 69 }))
                {
                    Controls = tokens;
                    _ = _header.Clear();
                    return index + 1;
                }
            }
            _ = _header.Clear();
        }
        return bytes.Length;
    }
}
