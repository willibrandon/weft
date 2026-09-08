namespace Weft.Protocol;

/// <summary>
/// Parameters for block.run.
/// </summary>
public sealed class BlockRunParams
{
    /// <summary>
    /// Gets the target session or tab to run in; null uses the most recent session.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the command and arguments.
    /// </summary>
    public required IReadOnlyList<string> Command { get; init; }

    /// <summary>
    /// Gets the working directory.
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Gets how long to wait for exit before returning with the block still running.
    /// </summary>
    public int TimeoutMs { get; init; } = 600_000;

    /// <summary>
    /// Gets the maximum bytes of output returned, split between head and tail.
    /// </summary>
    public int OutputBytesCap { get; init; } = 65_536;

    /// <summary>
    /// Gets whether to close the block after it exits.
    /// </summary>
    public bool Close { get; init; } = true;
}
