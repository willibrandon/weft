namespace Weft.Protocol;

/// <summary>
/// Payload of the hello event.
/// </summary>
public sealed class HelloData
{
    /// <summary>
    /// Gets the protocol version.
    /// </summary>
    public required int Protocol { get; init; }

    /// <summary>
    /// Gets the server version.
    /// </summary>
    public required string Server { get; init; }

    /// <summary>
    /// Gets the server process id.
    /// </summary>
    public required int Pid { get; init; }
}
