namespace Weft.Server;

/// <summary>
/// Startup options for a weft server process.
/// </summary>
public sealed class WeftServerOptions
{
    /// <summary>
    /// Gets the runtime directory that holds the control socket, block sockets, and lock file.
    /// </summary>
    public required string RuntimeDirectory { get; init; }

    /// <summary>
    /// Gets the directory that holds persisted session files.
    /// </summary>
    public required string StateDirectory { get; init; }
}
