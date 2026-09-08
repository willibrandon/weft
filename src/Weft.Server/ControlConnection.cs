using System.Net.Sockets;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// One control socket connection: requests in order, responses, and an optional event stream.
/// </summary>
internal sealed class ControlConnection
{
    private readonly long _id;
    private readonly Socket _socket;
    private readonly WeftServer _server;
    private readonly RequestDispatcher _dispatcher;
    private EventSubscription? _subscription;
    private Task? _pump;

    /// <summary>
    /// Initializes a connection over an accepted socket, which the connection owns.
    /// </summary>
    /// <param name="id">The connection id.</param>
    /// <param name="socket">The accepted socket.</param>
    /// <param name="server">The server.</param>
    /// <param name="dispatcher">The dispatcher.</param>
    internal ControlConnection(long id, Socket socket, WeftServer server, RequestDispatcher dispatcher)
    {
        _id = id;
        _socket = socket;
        _server = server;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Serves the connection until the peer closes it or the server stops.
    /// </summary>
    /// <param name="stopping">Signals server shutdown.</param>
    /// <returns>A task that completes when the connection is closed.</returns>
    internal async Task RunAsync(CancellationToken stopping)
    {
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var stream = new NetworkStream(_socket, ownsSocket: true);
        var writer = new ProtocolWriter(stream);
        try
        {
            await ServeAsync(stream, writer, closed).ConfigureAwait(false);
        }
        finally
        {
            await closed.CancelAsync().ConfigureAwait(false);
            _subscription?.Dispose();
            if (_pump is { } pump)
            {
                await pump.ConfigureAwait(false);
            }

            writer.Dispose();
            await _server.Registry.DetachConnectionAsync(_id, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ServeAsync(NetworkStream stream, ProtocolWriter writer, CancellationTokenSource closed)
    {
        var context = new RequestContext(_id, _server, subscription =>
        {
            _subscription?.Dispose();
            _subscription = subscription;
            _pump = PumpAsync(subscription, writer, closed.Token);
        });
        var reader = new ProtocolReader(stream);
        try
        {
            await writer.WriteAsync(ProtocolCodec.Event(
                ProtocolEvents.Hello,
                _server.Registry.Events.Seq,
                new HelloData { Protocol = ProtocolVersion.Current, Server = _server.Options.Version, Pid = Environment.ProcessId },
                ProtocolJsonContext.Default.HelloData), closed.Token).ConfigureAwait(false);
            while (!closed.IsCancellationRequested)
            {
                ProtocolMessage? request;
                try
                {
                    request = await reader.ReadAsync(closed.Token).ConfigureAwait(false);
                }
                catch (ProtocolException exception)
                {
                    await writer.WriteAsync(ProtocolCodec.Failure(null, exception.Code, exception.Message), closed.Token).ConfigureAwait(false);
                    continue;
                }

                if (request is null)
                {
                    break;
                }

                ProtocolMessage response = await _dispatcher.DispatchAsync(context, request, closed.Token).ConfigureAwait(false);
                await writer.WriteAsync(response, closed.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            ServerLog.Debug("ServeAsync ignored OperationCanceledException.");
        }
        catch (IOException)
        {
            ServerLog.Debug("ServeAsync ignored IOException.");
        }
        catch (SocketException)
        {
            ServerLog.Debug("ServeAsync ignored SocketException.");
        }
        catch (ObjectDisposedException)
        {
            ServerLog.Debug("ServeAsync ignored ObjectDisposedException.");
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task PumpAsync(EventSubscription subscription, ProtocolWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ProtocolMessage message in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            ServerLog.Debug("PumpAsync ignored OperationCanceledException.");
        }
        catch (IOException)
        {
            ServerLog.Debug("PumpAsync ignored IOException.");
        }
        catch (ObjectDisposedException)
        {
            ServerLog.Debug("PumpAsync ignored ObjectDisposedException.");
        }
    }
}
