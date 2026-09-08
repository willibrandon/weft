namespace Weft.Protocol;

/// <summary>
/// Parameters for block.wait.
/// </summary>
public sealed class BlockWaitParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets a regular expression to wait for on any screen line.
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Gets whether to return when the process exits.
    /// </summary>
    public bool Exit { get; init; }

    /// <summary>
    /// Gets a revision to wait past; the wait returns once the block's revision exceeds it.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// Gets the timeout in milliseconds.
    /// </summary>
    public int TimeoutMs { get; init; } = 30_000;
}
