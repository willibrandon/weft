using System.Collections.Concurrent;
using Weft.Client;

namespace Weft.Mcp;

/// <summary>
/// Hands out control connections to tool calls, one per call in flight, opening them on demand.
/// </summary>
/// <remarks>
/// The server answers one request at a time per connection, so a shared connection would let a long
/// wait block every other tool. Each call leases its own connection and returns it afterwards. The
/// connect delegate is the same connect-or-start path the CLI uses, so a server that went away while
/// the agent kept the MCP process alive is started again on the next call.
/// </remarks>
public sealed class WeftBridge : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<ControlClient>> _connect;
    private readonly ConcurrentBag<ControlClient> _idle = [];

    /// <summary>
    /// Creates a bridge that opens connections through the given delegate.
    /// </summary>
    /// <param name="connect">Opens a control connection, starting the server first when it is not running.</param>
    public WeftBridge(Func<CancellationToken, Task<ControlClient>> connect)
    {
        ArgumentNullException.ThrowIfNull(connect);
        _connect = connect;
    }

    /// <summary>
    /// Leases a connection for one call, reusing an idle one when it is still open.
    /// </summary>
    /// <param name="cancellationToken">Cancels connecting.</param>
    /// <returns>The lease; dispose it to return the connection.</returns>
    public async Task<ControlLease> LeaseAsync(CancellationToken cancellationToken) =>
        new ControlLease(this, await AcquireAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Disposes every idle connection.
    /// </summary>
    /// <returns>A task that completes when the pool is empty.</returns>
    public async ValueTask DisposeAsync()
    {
        while (_idle.TryTake(out ControlClient? client))
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Takes a connection back; a closed one is dropped instead of pooled.
    /// </summary>
    /// <param name="client">The connection a lease is returning.</param>
    internal void Return(ControlClient client)
    {
        if (client.Closed.IsCompleted)
        {
            _ = client.DisposeAsync().AsTask();
            return;
        }

        _idle.Add(client);
    }

    private async Task<ControlClient> AcquireAsync(CancellationToken cancellationToken)
    {
        while (_idle.TryTake(out ControlClient? idle))
        {
            if (!idle.Closed.IsCompleted)
            {
                return idle;
            }

            await idle.DisposeAsync().ConfigureAwait(false);
        }

        return await _connect(cancellationToken).ConfigureAwait(false);
    }
}
