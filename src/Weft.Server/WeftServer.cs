using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// The weft server: owns sessions, listens on the control socket, and shuts down cleanly.
/// </summary>
public sealed class WeftServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly RequestDispatcher _dispatcher = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a server; nothing starts until <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="options">The options.</param>
    public WeftServer(WeftServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Events = new EventLog();
        Registry = new SessionRegistry(options, Events, new SessionStore(Path.Combine(options.StateDirectory, "sessions")));
        Waits = new WaitChannels();
        ServerHandlers.Register(_dispatcher);
        SessionHandlers.Register(_dispatcher);
        BlockHandlers.Register(_dispatcher);
        LayoutHandlers.Register(_dispatcher);
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public WeftServerOptions Options { get; }

    /// <summary>
    /// Gets when the server started.
    /// </summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>
    /// Gets the control socket path.
    /// </summary>
    public string SocketPath => WeftPaths.ControlSocketPath(Options.RuntimeDirectory);

    /// <summary>
    /// Gets the registry.
    /// </summary>
    internal SessionRegistry Registry { get; }

    /// <summary>
    /// Gets the event log.
    /// </summary>
    internal EventLog Events { get; }

    /// <summary>
    /// Gets the named wait channels.
    /// </summary>
    internal WaitChannels Waits { get; }

    /// <summary>
    /// Gets a token that is cancelled when shutdown begins.
    /// </summary>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>
    /// Runs the server until cancelled or asked to shut down.
    /// </summary>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes after shutdown.</returns>
    /// <exception cref="InvalidOperationException">Another server holds the runtime directory.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        PrepareDirectories();
        using var serverLock = ServerLock.TryAcquire(WeftPaths.LockFilePath(Options.RuntimeDirectory));
        if (serverLock is null)
        {
            throw new InvalidOperationException("Another weft server is running on " + Options.RuntimeDirectory + ".");
        }

        ServerLog.UseFile(Path.Combine(Options.StateDirectory, "server.log"));
        StartedAt = DateTimeOffset.Now;
        using CancellationTokenRegistration registration = cancellationToken.Register(RequestShutdown);
        var listener = new ControlListener(SocketPath, this, _dispatcher);
        try
        {
            await listener.RunAsync(_stopping.Token).ConfigureAwait(false);
        }
        finally
        {
            Events.Publish(ProtocolEvents.ServerStopping, EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult);
            await Registry.CloseAllAsync(CancellationToken.None).ConfigureAwait(false);
            _stopped.TrySetResult();
            ServerLog.Info("Server stopped.");
        }
    }

    /// <summary>
    /// Begins shutdown; <see cref="RunAsync"/> completes once sessions are closed.
    /// </summary>
    public void RequestShutdown()
    {
        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }
    }

    /// <summary>
    /// Requests shutdown and waits for it.
    /// </summary>
    /// <returns>A task that completes after shutdown.</returns>
    public async ValueTask DisposeAsync()
    {
        RequestShutdown();
        if (StartedAt != default)
        {
            await WaitForStopAsync().ConfigureAwait(false);
        }

        _stopping.Dispose();
    }

    private async Task WaitForStopAsync()
    {
        while (!_stopped.Task.IsCompleted)
        {
            await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void PrepareDirectories()
    {
        CreatePrivateDirectory(Options.RuntimeDirectory);
        CreatePrivateDirectory(WeftPaths.BlockSocketDirectory(Options.RuntimeDirectory));
        CreatePrivateDirectory(Options.StateDirectory);
        CreatePrivateDirectory(Path.Combine(Options.StateDirectory, "sessions"));
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
