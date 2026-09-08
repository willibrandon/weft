using Weft.Client;

namespace Weft.Mcp;

/// <summary>
/// A control connection borrowed from the bridge for one call, returned to the pool when disposed.
/// </summary>
public sealed class ControlLease : IDisposable
{
    private readonly WeftBridge _bridge;
    private readonly CancellationToken _cancellation;
    private ControlClient? _client;

    /// <summary>
    /// Wraps a connection the bridge handed out.
    /// </summary>
    /// <param name="bridge">The bridge to return the connection to.</param>
    /// <param name="client">The connection.</param>
    /// <param name="cancellation">The call's token; a cancelled call leaves its request running on the server.</param>
    internal ControlLease(WeftBridge bridge, ControlClient client, CancellationToken cancellation)
    {
        _bridge = bridge;
        _client = client;
        _cancellation = cancellation;
    }

    /// <summary>
    /// Gets the connection for the duration of the lease.
    /// </summary>
    public ControlClient Client => _client ?? throw new ObjectDisposedException(nameof(ControlLease));

    /// <summary>
    /// Returns the connection to the bridge, or drops it if it closed or the call was cancelled.
    /// </summary>
    /// <remarks>
    /// Cancelling a call only abandons the response on this side; the server keeps answering the request on
    /// this connection, so the connection cannot be reused until that finishes. It is dropped instead.
    /// </remarks>
    public void Dispose()
    {
        if (_client is { } client)
        {
            _client = null;
            _bridge.Return(client, reusable: !_cancellation.IsCancellationRequested);
        }
    }
}
