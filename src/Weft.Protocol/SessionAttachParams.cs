namespace Weft.Protocol;

/// <summary>
/// Parameters for session.attach.
/// </summary>
public sealed class SessionAttachParams
{
    /// <summary>
    /// Gets the target session; null attaches to the most recent session.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the client's viewport width in columns.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the client's viewport height in rows.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets whether the client's input must be discarded.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Gets a display name for the client.
    /// </summary>
    public string? Name { get; init; }
}
