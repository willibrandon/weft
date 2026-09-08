namespace Weft.Client;

/// <summary>
/// Options for attaching the interactive client to a session.
/// </summary>
public sealed class AttachOptions
{
    /// <summary>
    /// Gets the control socket path.
    /// </summary>
    public required string SocketPath { get; init; }

    /// <summary>
    /// Gets the session target; null attaches to the most recent session or creates one.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets whether input is discarded.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Gets the display name reported to the server.
    /// </summary>
    public string Name { get; init; } = Environment.MachineName;
}
