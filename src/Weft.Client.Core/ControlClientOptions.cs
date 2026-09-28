namespace Weft.Client;

/// <summary>
/// Connection options for a control client.
/// </summary>
public sealed class ControlClientOptions
{
    /// <summary>
    /// Gets the path of the server's control socket.
    /// </summary>
    public required string SocketPath { get; init; }
}
