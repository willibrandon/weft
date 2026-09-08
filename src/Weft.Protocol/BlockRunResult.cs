namespace Weft.Protocol;

/// <summary>
/// Result of block.run.
/// </summary>
public sealed class BlockRunResult
{
    /// <summary>
    /// Gets the block the command ran in.
    /// </summary>
    public required string Block { get; init; }

    /// <summary>
    /// Gets whether the process exited before the timeout.
    /// </summary>
    public required bool Completed { get; init; }

    /// <summary>
    /// Gets the exit code when completed.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the captured output: head and tail with an omission marker when truncated.
    /// </summary>
    public required string Output { get; init; }

    /// <summary>
    /// Gets whether output was truncated.
    /// </summary>
    public required bool Truncated { get; init; }

    /// <summary>
    /// Gets the total output bytes produced.
    /// </summary>
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Gets how many bytes were omitted from the middle.
    /// </summary>
    public required long OmittedBytes { get; init; }

    /// <summary>
    /// Gets the wall-clock duration in milliseconds.
    /// </summary>
    public required long DurationMs { get; init; }
}
