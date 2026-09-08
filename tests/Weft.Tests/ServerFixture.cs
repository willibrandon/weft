using Weft.Client;
using Weft.Server;

namespace Weft.Tests;

/// <summary>
/// A real weft server on a private temporary runtime directory, for tests that drive it over its socket.
/// </summary>
internal sealed class ServerFixture : IAsyncDisposable
{
    private readonly WeftServer _server;
    private readonly Task _run;

    private bool _keepState;

    private ServerFixture(string root, WeftServerOptions options)
    {
        Root = root;
        _server = new WeftServer(options);
        _run = _server.RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Gets the temporary root that holds the runtime and state directories.
    /// </summary>
    internal string Root { get; }

    /// <summary>
    /// Gets the control socket path.
    /// </summary>
    internal string SocketPath => _server.SocketPath;

    /// <summary>
    /// Starts a server on a fresh temporary root; call <see cref="WaitReadyAsync"/> before connecting.
    /// </summary>
    /// <returns>The fixture.</returns>
    internal static ServerFixture Start() =>
        Resume(Path.Combine(Path.GetTempPath(), "weft-test-" + Guid.NewGuid().ToString("N")[..10]));

    /// <summary>
    /// Starts a server on an existing root so stored sessions can be resurrected.
    /// </summary>
    /// <param name="root">The root directory.</param>
    /// <returns>The fixture.</returns>
    internal static ServerFixture Resume(string root)
    {
        var options = new WeftServerOptions
        {
            RuntimeDirectory = Path.Combine(root, "run"),
            StateDirectory = Path.Combine(root, "state"),
            HomeDirectory = root,
            DefaultShell = "/bin/sh",
            Scrollback = 500,
            DefaultWidth = 80,
            DefaultHeight = 24
        };
        return new ServerFixture(root, options);
    }

    /// <summary>
    /// Waits until the control socket accepts connections.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the socket exists.</returns>
    internal async Task WaitReadyAsync(CancellationToken cancellationToken)
    {
        while (!File.Exists(SocketPath))
        {
            if (_run.IsCompleted)
            {
                throw new InvalidOperationException("The server stopped before creating its socket.");
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens a control client on the server.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>The client.</returns>
    internal Task<ControlClient> ConnectAsync(CancellationToken cancellationToken) => ControlClient.ConnectAsync(SocketPath, cancellationToken);

    /// <summary>
    /// Stops the server but leaves its state on disk for a later resume.
    /// </summary>
    /// <returns>A task that completes when the server has stopped.</returns>
    internal async Task StopKeepingStateAsync()
    {
        _keepState = true;
        await _server.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the server and deletes the temporary root unless state was kept.
    /// </summary>
    /// <returns>A task that completes when everything is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync().ConfigureAwait(false);
        while (!_run.IsCompleted)
        {
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }

        if (_run.IsFaulted)
        {
            throw new InvalidOperationException("The server failed.", _run.Exception);
        }

        if (_keepState || Environment.GetEnvironmentVariable("WEFT_TEST_KEEP") is "1")
        {
            return;
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException exception)
        {
            ClientLog.Debug("Fixture cleanup skipped: " + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            ClientLog.Debug("Fixture cleanup skipped: " + exception.Message);
        }
    }
}
