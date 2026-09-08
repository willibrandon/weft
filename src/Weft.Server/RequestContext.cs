namespace Weft.Server;

/// <summary>
/// What a request handler can reach: the server, the registry, and the connection it runs on.
/// </summary>
internal sealed class RequestContext
{
    private readonly Action<EventSubscription> _startEventPump;

    /// <summary>
    /// Initializes a context for one connection.
    /// </summary>
    /// <param name="connectionId">The connection id.</param>
    /// <param name="server">The server.</param>
    /// <param name="startEventPump">Starts streaming a subscription's events to the connection.</param>
    internal RequestContext(long connectionId, WeftServer server, Action<EventSubscription> startEventPump)
    {
        ConnectionId = connectionId;
        Server = server;
        _startEventPump = startEventPump;
    }

    /// <summary>
    /// Gets the connection id.
    /// </summary>
    internal long ConnectionId { get; }

    /// <summary>
    /// Gets the server.
    /// </summary>
    internal WeftServer Server { get; }

    /// <summary>
    /// Gets the registry.
    /// </summary>
    internal SessionRegistry Registry => Server.Registry;

    /// <summary>
    /// Turns the connection into an event stream for a subscription.
    /// </summary>
    /// <param name="subscription">The subscription.</param>
    internal void StartEventPump(EventSubscription subscription) => _startEventPump(subscription);
}
