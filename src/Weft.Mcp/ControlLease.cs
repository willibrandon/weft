using Weft.Client;

namespace Weft.Mcp;

/// <summary>
/// A control connection borrowed from the bridge for one call, returned to the pool when disposed.
/// </summary>
public sealed class ControlLease : IDisposable
{
    private readonly WeftBridge _bridge;
    private ControlClient? _client;

    /// <summary>
    /// Wraps a connection the bridge handed out.
    /// </summary>
    /// <param name="bridge">The bridge to return the connection to.</param>
    /// <param name="client">The connection.</param>
    internal ControlLease(WeftBridge bridge, ControlClient client)
    {
        _bridge = bridge;
        _client = client;
    }

    /// <summary>
    /// Gets the connection for the duration of the lease.
    /// </summary>
    public ControlClient Client => _client ?? throw new ObjectDisposedException(nameof(ControlLease));

    /// <summary>
    /// Returns the connection to the bridge, or drops it if it closed meanwhile.
    /// </summary>
    public void Dispose()
    {
        if (_client is { } client)
        {
            _client = null;
            _bridge.Return(client);
        }
    }
}
