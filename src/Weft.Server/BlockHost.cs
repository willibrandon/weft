using Hex1b;
using Hex1b.Automation;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Hosts one block: a Hex1b terminal over a pseudo-terminal, served to clients through an
/// HMP1 socket, with the server holding the primary role through a layout authority peer.
/// </summary>
internal sealed class BlockHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly OutputRevisionFilter _revision = new();
    private readonly Hex1bTerminalChildProcess _process;
    private readonly Hex1bTerminal _terminal;
    private readonly LayoutAuthorityPeer _authority;
    private Task<int>? _runTask;
    private Task? _exitTask;
    private int _clientCount;

    /// <summary>
    /// Initializes a host without starting anything.
    /// </summary>
    /// <param name="socketPath">The HMP1 socket path.</param>
    /// <param name="command">The command file name.</param>
    /// <param name="arguments">The command arguments.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="environment">Extra environment variables.</param>
    /// <param name="width">The initial width in columns.</param>
    /// <param name="height">The initial height in rows.</param>
    /// <param name="scrollback">The scrollback capacity in rows.</param>
    internal BlockHost(
        string socketPath,
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Dictionary<string, string> environment,
        int width,
        int height,
        int scrollback)
    {
        SocketPath = socketPath;
        _process = new Hex1bTerminalChildProcess(command, [.. arguments], workingDirectory, environment, inheritEnvironment: true, width, height);
        _terminal = Hex1bTerminal.CreateBuilder()
            .WithDimensions(width, height)
            .WithScrollback(scrollback)
            .WithWorkload(_process)
            .WithHmp1UdsServer(socketPath, options =>
            {
                options.OnClientConnected = (_, _) =>
                {
                    Interlocked.Increment(ref _clientCount);
                    return Task.CompletedTask;
                };
                options.OnClientDisconnected = (_, _) =>
                {
                    Interlocked.Decrement(ref _clientCount);
                    return Task.CompletedTask;
                };
            })
            .AddWorkloadFilter(_revision)
            .Build();
        _terminal.WindowTitleChanged += title => TitleChanged?.Invoke(title);
        _authority = new LayoutAuthorityPeer(socketPath, width, height);
    }

    /// <summary>
    /// Raised when the process exits, with its exit code.
    /// </summary>
    internal event Action<int>? Exited;

    /// <summary>
    /// Raised when the terminal's window title changes.
    /// </summary>
    internal event Action<string>? TitleChanged;

    /// <summary>
    /// Gets the HMP1 socket path.
    /// </summary>
    internal string SocketPath { get; }

    /// <summary>
    /// Gets the output revision.
    /// </summary>
    internal long Revision => _revision.Revision;

    /// <summary>
    /// Gets the signal notified after each output batch.
    /// </summary>
    internal ChangeSignal OutputChanged => _revision.Changed;

    /// <summary>
    /// Gets the process id, or null before start.
    /// </summary>
    internal int? ProcessId => _process.HasStarted ? _process.ProcessId : null;

    /// <summary>
    /// Gets whether the process has exited.
    /// </summary>
    internal bool HasExited => _process.HasExited;

    /// <summary>
    /// Gets the exit code once exited.
    /// </summary>
    internal int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    /// <summary>
    /// Gets the number of HMP1 peers connected, including the layout authority.
    /// </summary>
    internal int ClientCount => Volatile.Read(ref _clientCount);

    /// <summary>
    /// Starts the terminal, the process, and the layout authority peer.
    /// </summary>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>A task that completes when the block is serving and the process is running.</returns>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        _runTask = _terminal.RunAsync(_stopping.Token);
        await _process.StartAsync(cancellationToken).ConfigureAwait(false);
        _exitTask = WatchExitAsync();
        await _authority.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes raw input to the process.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    internal ValueTask WriteInputAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        _process.WriteInputAsync(bytes, cancellationToken);

    /// <summary>
    /// Resizes the terminal and process through the layout authority.
    /// </summary>
    /// <param name="width">The width in columns.</param>
    /// <param name="height">The height in rows.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the resize has been sent.</returns>
    internal Task ResizeAsync(int width, int height, CancellationToken cancellationToken) =>
        _authority.ResizeAsync(width, height, cancellationToken);

    /// <summary>
    /// Sends a signal to the process.
    /// </summary>
    /// <param name="signal">The signal number.</param>
    internal void Kill(int signal)
    {
        if (_process.HasStarted && !_process.HasExited)
        {
            _process.Kill(signal);
        }
    }

    /// <summary>
    /// Captures the screen and up to the requested number of history lines.
    /// </summary>
    /// <param name="historyLines">The history lines to include.</param>
    /// <param name="format">The line format.</param>
    /// <returns>The capture.</returns>
    internal BlockCapture Capture(int historyLines, CaptureFormat format)
    {
        long revision = Revision;
        using Hex1bTerminalSnapshot snapshot = _terminal.CreateSnapshot(Math.Max(0, historyLines));
        List<string> lines = new(snapshot.Height);
        if (format == CaptureFormat.Ansi)
        {
            lines.AddRange(snapshot.ToAnsi().Split('\n'));
        }
        else
        {
            for (int y = 0; y < snapshot.Height; y++)
            {
                lines.Add(snapshot.GetLineTrimmed(y));
            }
        }

        return new BlockCapture(
            revision,
            snapshot.Width,
            snapshot.Height - snapshot.ScrollbackLineCount,
            snapshot.CursorX,
            snapshot.CursorY,
            snapshot.ScrollbackLineCount,
            lines,
            snapshot.ApplicationCursorKeysEnabled,
            snapshot.BracketedPasteEnabled);
    }

    /// <summary>
    /// Stops the process if running, closes the socket, and releases the terminal.
    /// </summary>
    /// <returns>A task that completes when everything is released.</returns>
    public async ValueTask DisposeAsync()
    {
        await _authority.DisposeAsync().ConfigureAwait(false);
        await TerminateProcessAsync().ConfigureAwait(false);
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_runTask is { } run)
        {
            try
            {
                await run.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException exception)
            {
                ServerLog.Warn("Terminal run loop ended with an error: " + exception.Message);
            }
        }

        if (_exitTask is { } exit)
        {
            await exit.ConfigureAwait(false);
        }

        await _terminal.DisposeAsync().ConfigureAwait(false);
        await _process.DisposeAsync().ConfigureAwait(false);
        _stopping.Dispose();
        try
        {
            File.Delete(SocketPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task TerminateProcessAsync()
    {
        if (!_process.HasStarted || _process.HasExited || _exitTask is not { } exit)
        {
            return;
        }

        // A closing terminal sends SIGHUP; interactive shells ignore SIGTERM but honour SIGHUP.
        Kill(1);
        if (await WaitForExitAsync(exit, TimeSpan.FromSeconds(2)).ConfigureAwait(false))
        {
            return;
        }

        Kill(9);
        await WaitForExitAsync(exit, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    private static async Task<bool> WaitForExitAsync(Task exit, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!exit.IsCompleted)
        {
            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }

    private async Task WatchExitAsync()
    {
        int exitCode;
        try
        {
            exitCode = await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            ServerLog.Warn("Could not wait for the block process: " + exception.Message);
            exitCode = -1;
        }

        Exited?.Invoke(exitCode);
    }
}
