using System.Text.Json.Serialization.Metadata;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Routes requests to handlers by method name and turns failures into error responses.
/// </summary>
internal sealed class RequestDispatcher
{
    private readonly Dictionary<string, RequestHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a raw handler.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="handler">The handler.</param>
    internal void Register(string method, RequestHandler handler) => _handlers[method] = handler;

    /// <summary>
    /// Registers a typed handler that decodes parameters and encodes the result.
    /// </summary>
    /// <typeparam name="TParams">The parameter type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameter type information.</param>
    /// <param name="result">The result type information.</param>
    /// <param name="handler">The handler.</param>
    internal void Register<TParams, TResult>(
        string method,
        JsonTypeInfo<TParams> parameters,
        JsonTypeInfo<TResult> result,
        Func<RequestContext, TParams, CancellationToken, Task<TResult>> handler)
    {
        _handlers[method] = async (context, request, cancellationToken) =>
        {
            TParams decoded = ProtocolCodec.FromElement(request.Params, parameters);
            TResult produced = await handler(context, decoded, cancellationToken).ConfigureAwait(false);
            return ProtocolCodec.Success(request.Id ?? 0, produced, result);
        };
    }

    /// <summary>
    /// Gets the registered method names.
    /// </summary>
    internal IReadOnlyCollection<string> Methods => _handlers.Keys;

    /// <summary>
    /// Dispatches a request, never throwing for handler failures.
    /// </summary>
    /// <param name="context">The connection context.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response.</returns>
    internal async ValueTask<ProtocolMessage> DispatchAsync(RequestContext context, ProtocolMessage request, CancellationToken cancellationToken)
    {
        if (request.Method is not { } method || request.Id is not { } id)
        {
            return ProtocolCodec.Failure(request.Id, ErrorCodes.InvalidRequest, "A request needs an id and a method.");
        }

        if (!_handlers.TryGetValue(method, out RequestHandler? handler))
        {
            return ProtocolCodec.Failure(id, ErrorCodes.UnknownMethod, "Unknown method " + method + ".");
        }

        try
        {
            return await handler(context, request, cancellationToken).ConfigureAwait(false);
        }
        catch (ProtocolException exception)
        {
            return ProtocolCodec.Failure(id, exception.Code, exception.Message);
        }
        catch (LayoutException exception)
        {
            return ProtocolCodec.Failure(id, ErrorCodes.Unavailable, exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProtocolCodec.Failure(id, ErrorCodes.Timeout, "The request timed out.");
        }
        catch (Exception exception) when (IsUnexpected(exception))
        {
            ServerLog.Error("Request " + method + " failed", exception);
            return ProtocolCodec.Failure(id, ErrorCodes.Internal, "The request failed: " + exception.Message);
        }
    }

    private static bool IsUnexpected(Exception exception) => exception is not OperationCanceledException;
}
