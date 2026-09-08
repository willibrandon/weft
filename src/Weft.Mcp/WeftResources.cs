using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using Weft.Client;
using Weft.Protocol;

namespace Weft.Mcp;

/// <summary>
/// Resources that expose block screens as readable documents.
/// </summary>
/// <param name="bridge">The shared control connection.</param>
[McpServerResourceType]
public sealed class WeftResources(WeftBridge bridge)
{
    /// <summary>
    /// Reads a block's screen text.
    /// </summary>
    /// <param name="id">The block id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The screen text.</returns>
    [McpServerResource(UriTemplate = "weft://block/{id}", Name = "Block screen", MimeType = "text/plain"), Description("The text on a block's screen right now.")]
    public async Task<string> BlockScreenAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using ControlLease lease = await bridge.LeaseAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = lease.Client;
            BlockCaptureResult capture = await client.CaptureAsync(new BlockCaptureParams { Target = id }, cancellationToken).ConfigureAwait(false);
            return string.Join('\n', capture.Lines);
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Reads the session list.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One line per session.</returns>
    [McpServerResource(UriTemplate = "weft://sessions", Name = "Sessions", MimeType = "text/plain"), Description("Every session with its tabs and blocks.")]
    public async Task<string> SessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using ControlLease lease = await bridge.LeaseAsync(cancellationToken).ConfigureAwait(false);
            ControlClient client = lease.Client;
            SessionListResult result = await client.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
            return string.Join('\n', result.Sessions.Select(session => string.Create(CultureInfo.InvariantCulture, $"{session.Name} ({session.Id}): {session.Tabs} tabs, {session.Blocks} blocks")));
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }
}
