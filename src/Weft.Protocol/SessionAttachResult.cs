namespace Weft.Protocol;

/// <summary>
/// Result of session.attach: the client record and a full snapshot of the session.
/// </summary>
public sealed class SessionAttachResult
{
    /// <summary>
    /// Gets the client record.
    /// </summary>
    public required ClientInfo Client { get; init; }

    /// <summary>
    /// Gets the session.
    /// </summary>
    public required SessionInfo Session { get; init; }

    /// <summary>
    /// Gets every tab in the session.
    /// </summary>
    public required IReadOnlyList<TabInfo> Tabs { get; init; }

    /// <summary>
    /// Gets every block in the session.
    /// </summary>
    public required IReadOnlyList<BlockInfo> Blocks { get; init; }

    /// <summary>
    /// Gets the active tab's geometry.
    /// </summary>
    public required LayoutInfo Layout { get; init; }

    /// <summary>
    /// Gets the event sequence number at the time of the snapshot, for events.subscribe.
    /// </summary>
    public required long Seq { get; init; }
}
