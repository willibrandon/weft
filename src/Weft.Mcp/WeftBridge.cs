using Weft.Client;

namespace Weft.Mcp;

/// <summary>
/// The control connection shared by every tool invocation of one MCP server.
/// </summary>
public sealed class WeftBridge : IAsyncDisposable
{
    private readonly string _socketPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ControlClient? _client;

    /// <summary>
    /// Initializes a bridge for a control socket.
    /// </summary>
    /// <param name="socketPath">The control socket path.</param>
    public WeftBridge(string socketPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketPath);
        _socketPath = socketPath;
    }

    /// <summary>
    /// Gets the control socket path.
    /// </summary>
    public string SocketPath => _socketPath;

    /// <summary>
    /// Gets a connected client, reconnecting after a dropped connection.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>The client.</returns>
    public async Task<ControlClient> ClientAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is { } existing && !existing.Closed.IsCompleted)
            {
                return existing;
            }

            if (_client is { } stale)
            {
                await stale.DisposeAsync().ConfigureAwait(false);
            }

            _client = await ControlClient.ConnectAsync(_socketPath, cancellationToken).ConfigureAwait(false);
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Closes the connection.
    /// </summary>
    /// <returns>A task that completes when closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_client is { } client)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }
}
