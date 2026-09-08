namespace Weft.Protocol;

/// <summary>
/// Result of server.info.
/// </summary>
public sealed class ServerInfoResult
{
    /// <summary>
    /// Gets the server version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the protocol version.
    /// </summary>
    public required int Protocol { get; init; }

    /// <summary>
    /// Gets the server process id.
    /// </summary>
    public required int Pid { get; init; }

    /// <summary>
    /// Gets when the server started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets the runtime directory holding sockets.
    /// </summary>
    public required string RuntimeDirectory { get; init; }

    /// <summary>
    /// Gets the number of sessions.
    /// </summary>
    public required int Sessions { get; init; }

    /// <summary>
    /// Gets the number of blocks across all sessions.
    /// </summary>
    public required int Blocks { get; init; }

    /// <summary>
    /// Gets the number of attached clients.
    /// </summary>
    public required int Clients { get; init; }
}
