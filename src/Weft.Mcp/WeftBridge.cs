using System.Diagnostics.CodeAnalysis;
using Weft.Client;

namespace Weft.Mcp;

/// <summary>
/// Hands out control connections to tool calls, one per call in flight, opening them on demand.
/// </summary>
/// <remarks>
/// The server answers one request at a time per connection, so a shared connection would let a long
/// wait block every other tool. Each call leases its own connection and returns it afterwards. A
/// bounded number of idle connections is kept for reuse; the rest of a burst is closed on return so
/// a long-lived process does not hold its peak concurrency open forever. The connect delegate is the
/// same connect-or-start path the CLI uses, so a server that went away while the agent kept the MCP
/// process alive is started again on the next call.
/// </remarks>
public sealed class WeftBridge : IAsyncDisposable
{
    /// <summary>
    /// The most idle connections kept for reuse; a connection returned beyond this is closed instead.
    /// </summary>
    internal const int IdleCapacity = 4;

    private readonly Func<CancellationToken, Task<ControlClient>> _connect;
    private readonly Lock _gate = new();
    private readonly Stack<ControlClient> _idle = new();

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
        new ControlLease(this, await AcquireAsync(cancellationToken).ConfigureAwait(false), cancellationToken);

    /// <summary>
    /// Disposes every idle connection.
    /// </summary>
    /// <returns>A task that completes when the pool is empty.</returns>
    public async ValueTask DisposeAsync()
    {
        while (TryTakeIdle(out ControlClient? client))
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Takes a connection back; one that closed, was abandoned, or exceeds the idle capacity is dropped.
    /// </summary>
    /// <param name="client">The connection a lease is returning.</param>
    /// <param name="reusable">Whether the call finished, so no request is still running on the connection.</param>
    internal void Return(ControlClient client, bool reusable)
    {
        if (reusable && !client.Closed.IsCompleted && TryKeepIdle(client))
        {
            return;
        }

        _ = client.DisposeAsync().AsTask();
    }

    private async Task<ControlClient> AcquireAsync(CancellationToken cancellationToken)
    {
        while (TryTakeIdle(out ControlClient? idle))
        {
            if (!idle.Closed.IsCompleted)
            {
                return idle;
            }

            await idle.DisposeAsync().ConfigureAwait(false);
        }

        return await _connect(cancellationToken).ConfigureAwait(false);
    }

    // The capacity check and the add happen under one lock, so concurrent returns cannot all see room.
    private bool TryKeepIdle(ControlClient client)
    {
        lock (_gate)
        {
            if (_idle.Count >= IdleCapacity)
            {
                return false;
            }

            _idle.Push(client);
            return true;
        }
    }

    private bool TryTakeIdle([NotNullWhen(true)] out ControlClient? client)
    {
        lock (_gate)
        {
            return _idle.TryPop(out client);
        }
    }
}
