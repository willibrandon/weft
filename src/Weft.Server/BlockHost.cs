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
    private readonly InputObservingWorkload _workload;
    private readonly Hmp1PresentationAdapter _presentation;
    private readonly Hex1bTerminal _terminal;
    private readonly LayoutAuthorityPeer _authority;
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _startError;
    private Task<int>? _runTask;
    private Task? _listenTask;
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
        _workload = new InputObservingWorkload(_process);
        _workload.InputWritten += bytes => InputReceived?.Invoke(bytes);
        _revision.Output += () => Output?.Invoke();
        _presentation = new Hmp1PresentationAdapter(width, height)
        {
            OnClientConnected = (_, _) =>
            {
                Interlocked.Increment(ref _clientCount);
                return Task.CompletedTask;
            },
            OnClientDisconnected = (_, _) =>
            {
                Interlocked.Decrement(ref _clientCount);
                return Task.CompletedTask;
            }
        };
        var options = new Hex1bTerminalOptions
        {
            Width = width,
            Height = height,
            WorkloadAdapter = _workload,
            PresentationAdapter = _presentation,
            ScrollbackCapacity = scrollback,
            RunCallback = RunProcessAsync
        };
        options.PresentationFilters.Add(_revision);
        _terminal = new Hex1bTerminal(options);
        _terminal.WindowTitleChanged += title => TitleChanged?.Invoke(title);
        _authority = new LayoutAuthorityPeer(socketPath, width, height);
    }

    /// <summary>
    /// Raised when the process exits, with its exit code, after its final output has been applied.
    /// </summary>
    internal event Action<int>? Exited;

    /// <summary>
    /// Raised when the terminal's window title changes.
    /// </summary>
    internal event Action<string>? TitleChanged;

    /// <summary>
    /// Raised with input that arrived through the terminal from an attached peer.
    /// </summary>
    internal event Action<ReadOnlyMemory<byte>>? InputReceived;

    /// <summary>
    /// Raised after every output batch.
    /// </summary>
    internal event Action? Output;

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
    /// Starts the socket listener, the terminal, the process, and the layout authority peer.
    /// </summary>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>A task that completes when the block is serving and the process is running.</returns>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        long started = Environment.TickCount64;
        _listenTask = ListenAsync(_stopping.Token);
        _runTask = _terminal.RunAsync(_stopping.Token);
        while (!_started.Task.IsCompleted)
        {
            if (_runTask.IsCompleted)
            {
                throw new InvalidOperationException("The block terminal stopped before its process started.");
            }

            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }

        if (_startError is { } error)
        {
            throw new InvalidOperationException("The block process could not be started: " + error.Message, error);
        }

        long processStarted = Environment.TickCount64;
        await _authority.StartAsync(cancellationToken).ConfigureAwait(false);
        ServerLog.Debug(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Block on {Path.GetFileName(SocketPath)} started: process {processStarted - started} ms, authority {Environment.TickCount64 - processStarted} ms."));
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
        int wanted = Math.Max(0, Math.Min(historyLines, _terminal.ScrollbackCount));
        using Hex1bTerminalSnapshot snapshot = _terminal.CreateSnapshot(wanted);
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
                ServerLog.Debug("DisposeAsync ignored OperationCanceledException.");
            }
            catch (InvalidOperationException exception)
            {
                ServerLog.Warn("Terminal run loop ended with an error: " + exception.Message);
            }
        }

        if (_listenTask is { } listen)
        {
            await listen.ConfigureAwait(false);
        }

        await _terminal.DisposeAsync().ConfigureAwait(false);
        await _presentation.DisposeAsync().ConfigureAwait(false);
        await _workload.DisposeAsync().ConfigureAwait(false);
        await _process.DisposeAsync().ConfigureAwait(false);
        _stopping.Dispose();
        try
        {
            File.Delete(SocketPath);
        }
        catch (IOException)
        {
            ServerLog.Debug("DisposeAsync ignored IOException.");
        }
        catch (UnauthorizedAccessException)
        {
            ServerLog.Debug("DisposeAsync ignored UnauthorizedAccessException.");
        }
    }

    private async Task<int> RunProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _process.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _startError = exception;
            _started.TrySetResult();
            return -1;
        }

        _started.TrySetResult();
        ThreadPoolReservation.Acquire();
        int exitCode;
        try
        {
            exitCode = await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            ServerLog.Warn("Could not wait for the block process: " + exception.Message);
            exitCode = -1;
        }
        finally
        {
            ThreadPoolReservation.Release();
        }

        // The pseudo-terminal may still hold output the process wrote just before exiting; keep the
        // pumps running until the screen has been quiet for a moment so captures see the final state.
        await DrainOutputAsync(cancellationToken).ConfigureAwait(false);
        _exited.TrySetResult(exitCode);
        Exited?.Invoke(exitCode);
        return exitCode;
    }

    private async Task DrainOutputAsync(CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + 1500;
        while (Environment.TickCount64 < deadline && !cancellationToken.IsCancellationRequested)
        {
            long before = _revision.Revision;
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            quiet.CancelAfter(80);
            try
            {
                await _revision.Changed.WaitAsync(quiet.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (_revision.Revision == before)
                {
                    return;
                }
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (Stream stream in Hmp1Transports.ListenUnixSocket(SocketPath, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                _ = AcceptAsync(stream, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            ServerLog.Debug("ListenAsync ignored OperationCanceledException.");
        }
        catch (IOException exception)
        {
            ServerLog.Warn("Block socket listener ended: " + exception.Message);
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            ServerLog.Warn("Block socket listener ended: " + exception.Message);
        }
    }

    private async Task AcceptAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await _presentation.AddClient(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException or System.Net.Sockets.SocketException)
        {
            ServerLog.Warn("A block peer failed to attach: " + exception.Message);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task TerminateProcessAsync()
    {
        if (!_process.HasStarted || _process.HasExited)
        {
            return;
        }

        // A closing terminal sends SIGHUP; interactive shells ignore SIGTERM but honour SIGHUP.
        Kill(1);
        if (await WaitForExitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
        {
            return;
        }

        Kill(9);
        await WaitForExitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    private async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!_exited.Task.IsCompleted && !_process.HasExited)
        {
            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }
}
