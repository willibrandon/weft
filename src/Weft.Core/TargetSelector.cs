using System.Globalization;

namespace Weft.Core;

/// <summary>
/// A parsed target in the form <c>session:tab.block</c>, where each part is optional and may be a name, an index, or a prefixed id.
/// </summary>
/// <param name="SessionName">The session name, when given by name.</param>
/// <param name="Session">The session id, when given by id.</param>
/// <param name="TabIndex">The tab index, when given by index.</param>
/// <param name="Tab">The tab id, when given by id.</param>
/// <param name="BlockIndex">The block index within its tab, when given by index.</param>
/// <param name="Block">The block id, when given by id.</param>
public sealed record TargetSelector(
    string? SessionName,
    SessionId? Session,
    int? TabIndex,
    TabId? Tab,
    int? BlockIndex,
    BlockId? Block)
{
    /// <summary>
    /// The selector that names nothing and resolves to the caller's current session, tab, and block.
    /// </summary>
    public static TargetSelector Current { get; } = new(null, null, null, null, null, null);

    /// <summary>
    /// Gets whether the selector names nothing.
    /// </summary>
    public bool IsEmpty => SessionName is null && Session is null && TabIndex is null && Tab is null && BlockIndex is null && Block is null;

    /// <summary>
    /// Parses a target. A bare token that looks like <c>s1</c>, <c>t2</c>, or <c>b3</c> is an id; digits after <c>:</c> or <c>.</c> are indices; anything else before <c>:</c> is a session name.
    /// </summary>
    /// <param name="text">The target text; null or empty means the current target.</param>
    /// <param name="selector">The parsed selector.</param>
    /// <returns>Whether the text was well formed.</returns>
    public static bool TryParse(string? text, out TargetSelector selector)
    {
        selector = Current;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        int colon = text.IndexOf(':', StringComparison.Ordinal);
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        if (colon < 0 && dot < 0)
        {
            if (BlockId.TryParse(text, out BlockId block))
            {
                selector = Current with { Block = block };
                return true;
            }

            if (TabId.TryParse(text, out TabId tab))
            {
                selector = Current with { Tab = tab };
                return true;
            }

            if (SessionId.TryParse(text, out SessionId session))
            {
                selector = Current with { Session = session };
                return true;
            }

            selector = Current with { SessionName = text };
            return true;
        }

        string sessionPart;
        string tabPart;
        string blockPart;
        if (colon >= 0)
        {
            sessionPart = text[..colon];
            string rest = text[(colon + 1)..];
            int restDot = rest.IndexOf('.', StringComparison.Ordinal);
            tabPart = restDot >= 0 ? rest[..restDot] : rest;
            blockPart = restDot >= 0 ? rest[(restDot + 1)..] : string.Empty;
        }
        else
        {
            sessionPart = string.Empty;
            tabPart = text[..dot];
            blockPart = text[(dot + 1)..];
        }

        if (sessionPart.Contains('.', StringComparison.Ordinal) || blockPart.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        string? sessionName = null;
        SessionId? sessionId = null;
        if (sessionPart.Length > 0)
        {
            if (SessionId.TryParse(sessionPart, out SessionId parsedSession))
            {
                sessionId = parsedSession;
            }
            else
            {
                sessionName = sessionPart;
            }
        }

        if (!TryParsePart(tabPart, TabId.Prefix, out int? tabIndex, out int? tabIdValue)
            || !TryParsePart(blockPart, BlockId.Prefix, out int? blockIndex, out int? blockIdValue))
        {
            return false;
        }

        selector = new TargetSelector(
            sessionName,
            sessionId,
            tabIndex,
            tabIdValue is { } tabId ? new TabId(tabId) : null,
            blockIndex,
            blockIdValue is { } blockId ? new BlockId(blockId) : null);
        return true;
    }

    private static bool TryParsePart(string part, char prefix, out int? index, out int? id)
    {
        index = null;
        id = null;
        if (part.Length == 0)
        {
            return true;
        }

        if (IdFormat.TryParse(part, prefix, out int value))
        {
            id = value;
            return true;
        }

        if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedIndex))
        {
            index = parsedIndex;
            return true;
        }

        return false;
    }
}
