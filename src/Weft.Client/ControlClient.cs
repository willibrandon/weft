using System.Net.Sockets;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Weft.Protocol;

namespace Weft.Client;

/// <summary>
/// A connection to the server's control socket: typed requests, responses, and a stream of events.
/// </summary>
public sealed class ControlClient : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly ProtocolWriter _writer;
    private readonly ProtocolReader _reader;
    private readonly CancellationTokenSource _closed = new();
    private readonly Channel<ProtocolMessage> _events = Channel.CreateUnbounded<ProtocolMessage>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _gate = new();
    private readonly Dictionary<long, TaskCompletionSource<ProtocolMessage>> _pending = [];
    private readonly Task _readLoop;
    private long _nextId;

    private ControlClient(Socket socket, HelloData hello)
    {
        _socket = socket;
        Hello = hello;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _writer = new ProtocolWriter(_stream);
        _reader = new ProtocolReader(_stream);
        _readLoop = ReadLoopAsync();
    }

    /// <summary>
    /// Gets the server's hello payload.
    /// </summary>
    public HelloData Hello { get; }

    /// <summary>
    /// Gets the events received on this connection after events.subscribe.
    /// </summary>
    public ChannelReader<ProtocolMessage> Events => _events.Reader;

    /// <summary>
    /// Gets a task that completes when the connection is closed by either side.
    /// </summary>
    public Task Closed => _readLoop;

    /// <summary>
    /// Connects to a control socket and reads the hello event.
    /// </summary>
    /// <param name="socketPath">The socket path.</param>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>The connected client.</returns>
    /// <exception cref="ProtocolException">The server did not send a valid hello.</exception>
    public static async Task<ControlClient> ConnectAsync(string socketPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketPath);
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken).ConfigureAwait(false);
            HelloData hello;
            using (var probe = new NetworkStream(socket, ownsSocket: false))
            {
                var reader = new ProtocolReader(probe);
                try
                {
                    ProtocolMessage first = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new ProtocolException(ErrorCodes.InvalidRequest, "The server closed the connection before hello.");
                    if (!string.Equals(first.Event, ProtocolEvents.Hello, StringComparison.Ordinal))
                    {
                        throw new ProtocolException(ErrorCodes.InvalidRequest, "The server did not send hello first.");
                    }

                    hello = ProtocolCodec.FromElement(first.Data, ProtocolJsonContext.Default.HelloData);
                }
                finally
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }

            return new ControlClient(socket, hello);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends a request and awaits its result.
    /// </summary>
    /// <typeparam name="TParams">The parameter type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="parameterInfo">The parameter type information.</param>
    /// <param name="resultInfo">The result type information.</param>
    /// <param name="cancellationToken">Cancels the wait for a response.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ProtocolException">The server returned an error.</exception>
    public async Task<TResult> InvokeAsync<TParams, TResult>(
        string method,
        TParams parameters,
        JsonTypeInfo<TParams> parameterInfo,
        JsonTypeInfo<TResult> resultInfo,
        CancellationToken cancellationToken)
    {
        long id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<ProtocolMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending[id] = completion;
        }

        try
        {
            await _writer.WriteAsync(ProtocolCodec.Request(id, method, parameters, parameterInfo), cancellationToken).ConfigureAwait(false);
            ProtocolMessage response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (response.Error is { } error)
            {
                throw new ProtocolException(error.Code, error.Message);
            }

            return ProtocolCodec.FromElement(response.Result, resultInfo);
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(id);
            }
        }
    }

    /// <summary>
    /// Closes the connection.
    /// </summary>
    /// <returns>A task that completes when the socket is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        await _closed.CancelAsync().ConfigureAwait(false);
        _socket.Dispose();
        while (!_readLoop.IsCompleted)
        {
            await Task.Delay(5, CancellationToken.None).ConfigureAwait(false);
        }

        await _reader.DisposeAsync().ConfigureAwait(false);
        _writer.Dispose();
        _stream.Dispose();
        _closed.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_closed.IsCancellationRequested)
            {
                ProtocolMessage? message = await _reader.ReadAsync(_closed.Token).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (message.IsEvent)
                {
                    _events.Writer.TryWrite(message);
                    continue;
                }

                if (message.Id is { } id)
                {
                    TaskCompletionSource<ProtocolMessage>? completion;
                    lock (_gate)
                    {
                        _pending.TryGetValue(id, out completion);
                    }

                    completion?.TrySetResult(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            ClientLog.Debug("ReadLoopAsync ignored OperationCanceledException.");
        }
        catch (IOException)
        {
            ClientLog.Debug("ReadLoopAsync ignored IOException.");
        }
        catch (SocketException)
        {
            ClientLog.Debug("ReadLoopAsync ignored SocketException.");
        }
        catch (ObjectDisposedException)
        {
            ClientLog.Debug("ReadLoopAsync ignored ObjectDisposedException.");
        }
        catch (ProtocolException)
        {
            ClientLog.Debug("ReadLoopAsync ignored ProtocolException.");
        }
        finally
        {
            _events.Writer.TryComplete();
            List<TaskCompletionSource<ProtocolMessage>> pending;
            lock (_gate)
            {
                pending = [.. _pending.Values];
                _pending.Clear();
            }

            foreach (TaskCompletionSource<ProtocolMessage> completion in pending)
            {
                completion.TrySetResult(ProtocolCodec.Failure(0, ErrorCodes.Unavailable, "The connection closed."));
            }
        }
    }
}
