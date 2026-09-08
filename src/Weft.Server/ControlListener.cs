using System.Net.Sockets;

namespace Weft.Server;

/// <summary>
/// Accepts control socket connections and serves each on its own task.
/// </summary>
internal sealed class ControlListener
{
    private readonly string _socketPath;
    private readonly WeftServer _server;
    private readonly RequestDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private readonly HashSet<Task> _connections = [];
    private long _nextId;

    /// <summary>
    /// Initializes a listener.
    /// </summary>
    /// <param name="socketPath">The socket path.</param>
    /// <param name="server">The server.</param>
    /// <param name="dispatcher">The dispatcher.</param>
    internal ControlListener(string socketPath, WeftServer server, RequestDispatcher dispatcher)
    {
        _socketPath = socketPath;
        _server = server;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Listens until cancelled, then waits for connections to finish.
    /// </summary>
    /// <param name="stopping">Signals shutdown.</param>
    /// <returns>A task that completes when the listener has closed.</returns>
    internal async Task RunAsync(CancellationToken stopping)
    {
        File.Delete(_socketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        listener.Listen(64);
        ServerLog.Info("Listening on " + _socketPath);
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                Socket accepted;
                try
                {
                    accepted = await listener.AcceptAsync(stopping).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException exception)
                {
                    ServerLog.Warn("Accept failed: " + exception.Message);
                    continue;
                }

                Track(new ControlConnection(Interlocked.Increment(ref _nextId), accepted, _server, _dispatcher), stopping);
            }
        }
        finally
        {
            Task[] pending;
            lock (_gate)
            {
                pending = [.. _connections];
            }

            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (pending.Length > 0 && !drain.IsCancellationRequested)
            {
                await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
                pending = [.. pending.Where(task => !task.IsCompleted)];
            }

            if (pending.Length > 0)
            {
                ServerLog.Warn("Some connections did not close within five seconds.");
            }

            File.Delete(_socketPath);
        }
    }

    private void Track(ControlConnection connection, CancellationToken stopping)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _connections.Add(completion.Task);
        }

        _ = ServeAsync(connection, completion, stopping);
    }

    private async Task ServeAsync(ControlConnection connection, TaskCompletionSource completion, CancellationToken stopping)
    {
        try
        {
            await connection.RunAsync(stopping).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ServerLog.Error("A connection failed", exception);
        }
        finally
        {
            lock (_gate)
            {
                _connections.Remove(completion.Task);
            }

            completion.TrySetResult();
        }
    }
}
