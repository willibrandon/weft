namespace Weft.Protocol;

/// <summary>
/// Payload of session events.
/// </summary>
public sealed class SessionEventData
{
    /// <summary>
    /// Gets the session.
    /// </summary>
    public required SessionInfo Session { get; init; }
}
