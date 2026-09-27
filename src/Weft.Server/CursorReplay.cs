using Hex1b.Tokens;

namespace Weft.Server;

/// <summary>
/// Projects ordinary saved-cursor restores to server coordinates for views that joined after the save.
/// </summary>
internal sealed class CursorReplay
{
    private readonly Lock _gate = new();
    private int _width;
    private bool _originMode;
    private bool _marginMode;
    private bool _canPositionRestore;

    /// <summary>
    /// Updates the right boundary used to preserve pending wrap during cursor restores.
    /// </summary>
    /// <param name="width">The new terminal width.</param>
    internal void Resize(int width)
    {
        lock (_gate)
        {
            _width = width;
        }
    }

    /// <summary>
    /// Preserves output tokens and makes ordinary cursor restores independent of a viewer's earlier output.
    /// </summary>
    /// <param name="appliedTokens">Tokens applied to the authoritative server terminal.</param>
    /// <returns>The tokens to send to attached views.</returns>
    internal IReadOnlyList<AnsiToken> Project(IReadOnlyList<AppliedToken> appliedTokens)
    {
        lock (_gate)
        {
            List<AnsiToken> tokens = [with(appliedTokens.Count)];
            foreach (AppliedToken applied in appliedTokens)
            {
                tokens.Add(applied.Token);
                switch (applied.Token)
                {
                    case SaveCursorToken:
                        // A right-edge save can carry pending wrap. Explicit positioning would
                        // clear it, so retain that sequence unchanged, as with margin-based saves.
                        _canPositionRestore = !_originMode && !_marginMode && applied.CursorXAfter < _width - 1;
                        break;
                    case RestoreCursorToken when _canPositionRestore && !_originMode && !_marginMode && applied.CursorXAfter < _width - 1:
                        // A new view may never have seen the save. Keep the restore for its other
                        // state, then use the server's resulting position. VPA/CHA leave soft wraps
                        // intact; CUP can mark the preceding row as a hard line boundary.
                        tokens.Add(new CursorRowToken(applied.CursorYAfter + 1));
                        tokens.Add(new CursorColumnToken(applied.CursorXAfter + 1));
                        break;
                    case PrivateModeToken { Mode: 6 } mode:
                        _originMode = mode.Enable;
                        break;
                    case PrivateModeToken { Mode: 69 } mode:
                        _marginMode = mode.Enable;
                        break;
                    case SoftResetToken or RisToken:
                        _originMode = false;
                        _marginMode = false;
                        _canPositionRestore = false;
                        break;
                    default:
                        break;
                }
            }

            return tokens;
        }
    }
}
