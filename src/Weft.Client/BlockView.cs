using Hex1b;

namespace Weft.Client;

/// <summary>
/// The client-side terminal for one block: an HMP1 secondary peer rendered through a terminal widget.
/// </summary>
internal sealed class BlockView : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Hex1bTerminal _terminal;
    private readonly Task _run;
    private int _width;
    private int _height;

    private BlockView(string id, Hex1bTerminal terminal, TerminalWidgetHandle handle, int width, int height)
    {
        Id = id;
        _terminal = terminal;
        Handle = handle;
        _width = width;
        _height = height;
        _run = RunAsync();
    }

    /// <summary>
    /// Gets the block id.
    /// </summary>
    internal string Id { get; }

    /// <summary>
    /// Gets the widget handle to render.
    /// </summary>
    internal TerminalWidgetHandle Handle { get; }

    /// <summary>
    /// Gets whether the HMP1 connection is up.
    /// </summary>
    internal bool Connected { get; private set; }

    /// <summary>
    /// Gets whether the connection ended.
    /// </summary>
    internal bool Disconnected { get; private set; }

    /// <summary>
    /// Connects to a block's socket and starts rendering.
    /// </summary>
    /// <param name="id">The block id.</param>
    /// <param name="socketPath">The block's HMP1 socket path.</param>
    /// <param name="width">The block's terminal width.</param>
    /// <param name="height">The block's terminal height.</param>
    /// <param name="displayName">The peer name reported to the block.</param>
    /// <param name="invalidate">Requests a redraw.</param>
    /// <returns>The view.</returns>
    internal static BlockView Start(string id, string socketPath, int width, int height, string displayName, Action invalidate)
    {
        BlockView? created = null;
        Hex1bTerminal terminal = Hex1bTerminal.CreateBuilder()
            .WithDimensions(Math.Max(1, width), Math.Max(1, height))
            .WithHmp1UdsClient(socketPath, options =>
            {
                options.DisplayName = displayName;
                options.DefaultRole = Hmp1Role.Secondary;
                options.OnConnected = (_, _) =>
                {
                    if (created is { } view)
                    {
                        view.Connected = true;
                    }

                    invalidate();
                    return Task.CompletedTask;
                };
                options.OnRemoteResized = (args, _) =>
                {
                    created?.Resize(args.Width, args.Height);
                    invalidate();
                    return Task.CompletedTask;
                };
                options.OnDisconnected = _ =>
                {
                    if (created is { } view)
                    {
                        view.Disconnected = true;
                    }

                    invalidate();
                    return Task.CompletedTask;
                };
            })
            .WithScrollback(5000)
            .WithTerminalWidget(out TerminalWidgetHandle handle)
            .Build();
        created = new BlockView(id, terminal, handle, width, height);
        return created;
    }

    /// <summary>
    /// Gets the block's terminal width.
    /// </summary>
    internal int Width => _width;

    /// <summary>
    /// Gets the block's terminal height.
    /// </summary>
    internal int Height => _height;

    /// <summary>
    /// Resizes the local terminal to match the block.
    /// </summary>
    /// <param name="width">The width in columns.</param>
    /// <param name="height">The height in rows.</param>
    internal void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;
        _terminal.Resize(width, height);
    }

    /// <summary>
    /// Disconnects and releases the local terminal.
    /// </summary>
    /// <returns>A task that completes when released.</returns>
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        while (!_run.IsCompleted)
        {
            await Task.Delay(5, CancellationToken.None).ConfigureAwait(false);
        }

        await _terminal.DisposeAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await _terminal.RunAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            Disconnected = true;
        }
    }
}
