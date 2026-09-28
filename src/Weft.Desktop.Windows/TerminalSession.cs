using Microsoft.UI.Dispatching;
using Weft.Client;
using Weft.Core;

namespace Weft.Desktop.Windows;

/// <summary>
/// Owns one window's session attachment and delivers its frames on the UI thread.
/// </summary>
/// <remarks>
/// The worker keeps only the newest frame, so a busy terminal cannot queue stale work. Closing the session
/// releases the window's control and terminal connections; the server keeps every process running.
/// </remarks>
internal sealed partial class TerminalSession : IDisposable
{
    private readonly DesktopClient _client;
    private readonly DispatcherQueue _dispatcher;
    private int _scheduled;
    private bool _disposed;

    /// <summary>
    /// Starts attaching without blocking the UI thread.
    /// </summary>
    /// <param name="columns">The initial columns.</param>
    /// <param name="rows">The initial rows.</param>
    /// <param name="serverPath">The server to start when none is running, or null for the bundled server.</param>
    internal TerminalSession(int columns, int rows, string? serverPath = null)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        // The configured shell is read when a window opens and applies to terminals it creates.
        WeftConfig config = WeftConfigLoader.LoadDefault(out string? error);
        ConfigurationError = error;
        string server = serverPath ?? Environment.GetEnvironmentVariable("WEFT_DESKTOP_SERVER") ?? DefaultServer;
        _client = new DesktopClient(WeftPaths.ResolveRuntimeDirectory(), server, Math.Clamp(columns, 4, 500), Math.Clamp(rows, 4, 300),
            error is null ? config.Shell : null);
        _client.SetFrameReady(Schedule);
    }

    /// <summary>
    /// Raised on the UI thread with the newest frame.
    /// </summary>
    internal event Action<DesktopFrame>? FrameArrived;

    /// <summary>
    /// Gets the configuration error, if the user's Weft configuration could not be read.
    /// </summary>
    internal string? ConfigurationError { get; }

    /// <summary>
    /// Gets the bundled server, which is distinct from the app executable.
    /// </summary>
    internal static string DefaultServer { get; } = Path.Join(AppContext.BaseDirectory, "weft-server.exe");

    /// <summary>
    /// Queues a command once; rejected or uncertain input is never retried here.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>Whether the command was accepted.</returns>
    internal bool Send(DesktopCommand command)
    {
        return !_disposed && _client.TrySend(command);
    }

    /// <summary>
    /// Stops notifications and releases the attachment without ending the session's processes.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FrameArrived = null;
        _client.SetFrameReady(null);
        _ = ReleaseAsync(_client);
    }

    private void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 0 && !_dispatcher.TryEnqueue(Drain))
        {
            Volatile.Write(ref _scheduled, 0);
        }
    }

    private void Drain()
    {
        Volatile.Write(ref _scheduled, 0);
        if (!_disposed && _client.TakeFrame() is { } frame)
        {
            FrameArrived?.Invoke(frame);
        }
    }

    private static async Task ReleaseAsync(DesktopClient client)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
        {
            ClientLog.Debug(exception.ToString());
        }
    }
}
