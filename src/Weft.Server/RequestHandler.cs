using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Handles one request and produces its response.
/// </summary>
/// <param name="context">The connection context.</param>
/// <param name="request">The request.</param>
/// <param name="cancellationToken">Cancels the request when the connection or server stops.</param>
/// <returns>The response.</returns>
internal delegate ValueTask<ProtocolMessage> RequestHandler(RequestContext context, ProtocolMessage request, CancellationToken cancellationToken);
