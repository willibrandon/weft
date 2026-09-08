namespace Weft.Protocol;

/// <summary>
/// The event names the server emits.
/// </summary>
public static class ProtocolEvents
{
    /// <summary>The first event on every connection.</summary>
    public const string Hello = "hello";

    /// <summary>A session was created.</summary>
    public const string SessionCreated = "session.created";

    /// <summary>A session was renamed.</summary>
    public const string SessionRenamed = "session.renamed";

    /// <summary>A session was closed.</summary>
    public const string SessionClosed = "session.closed";

    /// <summary>A session's authoritative size changed.</summary>
    public const string SessionResized = "session.resized";

    /// <summary>A tab was created.</summary>
    public const string TabCreated = "tab.created";

    /// <summary>A tab became the active tab of its session.</summary>
    public const string TabSelected = "tab.selected";

    /// <summary>A tab was renamed.</summary>
    public const string TabRenamed = "tab.renamed";

    /// <summary>A tab was closed.</summary>
    public const string TabClosed = "tab.closed";

    /// <summary>A block was created.</summary>
    public const string BlockCreated = "block.created";

    /// <summary>A block's title changed.</summary>
    public const string BlockTitled = "block.titled";

    /// <summary>A block's process exited.</summary>
    public const string BlockExited = "block.exited";

    /// <summary>A block was closed.</summary>
    public const string BlockClosed = "block.closed";

    /// <summary>A block became the active block of its tab.</summary>
    public const string BlockFocused = "block.focused";

    /// <summary>A block produced output; throttled and carrying the latest revision.</summary>
    public const string BlockOutput = "block.output";

    /// <summary>A tab's geometry changed.</summary>
    public const string LayoutChanged = "layout.changed";

    /// <summary>A client attached to a session.</summary>
    public const string ClientAttached = "client.attached";

    /// <summary>A client detached from a session.</summary>
    public const string ClientDetached = "client.detached";

    /// <summary>The subscriber fell behind and events were dropped.</summary>
    public const string SubscriberPaused = "subscriber.paused";

    /// <summary>The subscriber caught up after a pause.</summary>
    public const string SubscriberResumed = "subscriber.resumed";

    /// <summary>The server is shutting down.</summary>
    public const string ServerStopping = "server.stopping";
}
