namespace Weft.Protocol;

/// <summary>
/// Parameters for block.kill.
/// </summary>
public sealed class BlockKillParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the signal number; defaults to SIGTERM.
    /// </summary>
    public int Signal { get; init; } = 15;
}
