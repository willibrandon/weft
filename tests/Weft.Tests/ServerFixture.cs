using Weft.Client;
using Weft.Protocol;
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
        Resume(Path.Join(Path.GetTempPath(), "weft-test-" + Guid.NewGuid().ToString("N")[..10]));

    /// <summary>
    /// Starts a server on an existing root so stored sessions can be resurrected.
    /// </summary>
    /// <param name="root">The root directory.</param>
    /// <returns>The fixture.</returns>
    internal static ServerFixture Resume(string root)
    {
        var options = new WeftServerOptions
        {
            RuntimeDirectory = Path.Join(root, "run"),
            StateDirectory = Path.Join(root, "state"),
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
    /// Waits until a block's shell has printed a prompt, so keys typed next are read by a ready shell.
    /// </summary>
    /// <param name="client">The control client.</param>
    /// <param name="target">The block.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when a prompt is on screen.</returns>
    internal static async Task WaitForPromptAsync(ControlClient client, string target, CancellationToken cancellationToken)
    {
        BlockWaitResult prompt = await client.WaitAsync(new BlockWaitParams { Target = target, Pattern = "\\$\\s*$", TimeoutMs = 20_000 }, cancellationToken).ConfigureAwait(false);
        if (prompt.Outcome != WaitOutcome.Pattern)
        {
            BlockCaptureResult screen = await client.CaptureAsync(new BlockCaptureParams { Target = target }, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("No prompt appeared in " + target + " within 20 seconds. Screen: [" + string.Join("\u23ce", screen.Lines).Trim() + "]");
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
