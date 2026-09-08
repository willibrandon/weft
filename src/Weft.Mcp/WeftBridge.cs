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

    private bool _disposed;

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
    public async Task<ControlLease> LeaseAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        ControlClient client = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        bool live;
        lock (_gate)
        {
            live = !_disposed;
        }

        if (live)
        {
            return new ControlLease(this, client, cancellationToken);
        }

        // Disposal won the race while the connection was being opened, so the connection is closed
        // rather than handed to a caller of a bridge that is gone.
        await client.DisposeAsync().ConfigureAwait(false);
        throw new ObjectDisposedException(nameof(WeftBridge));
    }

    /// <summary>
    /// Disposes every idle connection.
    /// </summary>
    /// <returns>A task that completes when the pool is empty.</returns>
    public async ValueTask DisposeAsync()
    {
        // Marked under the lock, so a lease returned after this point closes its connection instead of
        // pooling it into a bridge nothing will drain again.
        List<ControlClient> idle;
        lock (_gate)
        {
            _disposed = true;
            idle = [.. _idle];
            _idle.Clear();
        }

        foreach (ControlClient client in idle)
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
            if (_disposed || _idle.Count >= IdleCapacity)
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
