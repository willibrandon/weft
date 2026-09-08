namespace Weft.Protocol;

/// <summary>
/// Result of session.list.
/// </summary>
public sealed class SessionListResult
{
    /// <summary>
    /// Gets the sessions ordered by creation.
    /// </summary>
    public required IReadOnlyList<SessionInfo> Sessions { get; init; }
}
