namespace Weft.Protocol;

/// <summary>
/// Parameters for methods that act on an attached client.
/// </summary>
public sealed class ClientParams
{
    /// <summary>
    /// Gets the client id from session.attach.
    /// </summary>
    public required string Client { get; init; }
}
