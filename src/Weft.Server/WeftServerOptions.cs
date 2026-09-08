using Weft.Core;

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

    /// <summary>
    /// Gets the directory new sessions start in when none is given.
    /// </summary>
    public string HomeDirectory { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Gets the shell for new blocks, or null to use the environment's shell.
    /// </summary>
    public string? DefaultShell { get; init; }

    /// <summary>
    /// Gets the cells reserved on every side of a block for its frame; zero for frameless layouts.
    /// </summary>
    public int FrameSize { get; init; } = 1;

    /// <summary>
    /// Gets the scrollback rows kept per block.
    /// </summary>
    public int Scrollback { get; init; } = 10_000;

    /// <summary>
    /// Gets the width used for sessions with no client.
    /// </summary>
    public int DefaultWidth { get; init; } = 120;

    /// <summary>
    /// Gets the height used for sessions with no client.
    /// </summary>
    public int DefaultHeight { get; init; } = 36;

    /// <summary>
    /// Gets the size policy for new sessions.
    /// </summary>
    public SizePolicy DefaultSizePolicy { get; init; }

    /// <summary>
    /// Gets the server version string reported to clients.
    /// </summary>
    public string Version { get; init; } = "0.1.0";

    /// <summary>
    /// Gets hooks: event name to a shell command run with the event in environment variables.
    /// </summary>
    public IReadOnlyDictionary<string, string> Hooks { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
