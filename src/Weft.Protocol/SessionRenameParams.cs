namespace Weft.Protocol;

/// <summary>
/// Parameters for session.rename.
/// </summary>
public sealed class SessionRenameParams
{
    /// <summary>
    /// Gets the target session.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the new name.
    /// </summary>
    public required string Name { get; init; }
}
