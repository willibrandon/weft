using Hex1b;

namespace Weft.Server;

/// <summary>
/// An in-process HMP1 peer that holds the primary role on a block so the server, not any
/// attached client, decides the block's size.
/// </summary>
internal sealed class LayoutAuthorityPeer : IAsyncDisposable
{
    private readonly string _socketPath;
    private readonly CancellationTokenSource _stopping = new();
    private Hmp1WorkloadAdapter? _adapter;
    private Task? _drainTask;
    private int _width;
    private int _height;

    /// <summary>
    /// Initializes a peer for a block socket.
    /// </summary>
    /// <param name="socketPath">The block's HMP1 socket path.</param>
    /// <param name="width">The initial width to assert.</param>
    /// <param name="height">The initial height to assert.</param>
    internal LayoutAuthorityPeer(string socketPath, int width, int height)
    {
        _socketPath = socketPath;
        _width = width;
        _height = height;
    }

    /// <summary>
    /// Connects to the block socket, takes the primary role, and starts draining output.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>A task that completes once the peer is primary.</returns>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromMilliseconds(20),
            MaxDelay = TimeSpan.FromMilliseconds(250),
            MaxAttempts = 200
        };
        var options = new Hmp1ClientOptions
        {
            StreamFactory = Hmp1Transports.RetryingUnixSocket(_socketPath, policy),
            DisplayName = "weft-layout",
            DefaultRole = Hmp1Role.Primary,
            OnRoleChanged = OnRoleChangedAsync
        };
        _adapter = new Hmp1WorkloadAdapter(options);
        await _adapter.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _drainTask = DrainAsync(_adapter, _stopping.Token);
        await _adapter.RequestPrimaryAsync(_width, _height, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asserts a new size for the block.
    /// </summary>
    /// <param name="width">The width in columns.</param>
    /// <param name="height">The height in rows.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the resize frame has been sent.</returns>
    internal async Task ResizeAsync(int width, int height, CancellationToken cancellationToken)
    {
        _width = width;
        _height = height;
        if (_adapter is not { } adapter)
        {
            return;
        }

        if (!adapter.IsPrimary)
        {
            await adapter.RequestPrimaryAsync(width, height, cancellationToken).ConfigureAwait(false);
            return;
        }

        await adapter.ResizeAsync(width, height, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Disconnects the peer.
    /// </summary>
    /// <returns>A task that completes when the connection is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_adapter is not null)
        {
            await _adapter.DisposeAsync().ConfigureAwait(false);
            _adapter = null;
        }

        if (_drainTask is { } drain)
        {
            try
            {
                await drain.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ServerLog.Debug("DisposeAsync ignored OperationCanceledException.");
            }
        }

        _stopping.Dispose();
    }

    private static async Task DrainAsync(Hmp1WorkloadAdapter adapter, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadOnlyMemory<byte> chunk = await adapter.ReadOutputAsync(cancellationToken).ConfigureAwait(false);
                if (chunk.IsEmpty)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            ServerLog.Debug("DrainAsync ignored OperationCanceledException.");
        }
        catch (IOException exception)
        {
            ServerLog.Warn("Layout peer stream ended: " + exception.Message);
        }
        catch (ObjectDisposedException)
        {
            ServerLog.Debug("DrainAsync ignored ObjectDisposedException.");
        }
    }

    private async Task OnRoleChangedAsync(RoleChangedEventArgs args, CancellationToken cancellationToken)
    {
        if (args.NowPrimary || _adapter is not { } adapter || _stopping.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            await adapter.RequestPrimaryAsync(_width, _height, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ServerLog.Debug("OnRoleChangedAsync ignored OperationCanceledException.");
        }
        catch (IOException exception)
        {
            ServerLog.Warn("Layout peer could not reclaim primary: " + exception.Message);
        }
    }
}
