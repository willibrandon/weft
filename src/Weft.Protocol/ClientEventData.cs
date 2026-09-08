namespace Weft.Protocol;

/// <summary>
/// Payload of client events.
/// </summary>
public sealed class ClientEventData
{
    /// <summary>
    /// Gets the client.
    /// </summary>
    public required ClientInfo Client { get; init; }
}
