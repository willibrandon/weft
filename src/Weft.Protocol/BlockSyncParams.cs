namespace Weft.Protocol;

/// <summary>
/// Parameters for block.sync.
/// </summary>
public sealed class BlockSyncParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets whether the block is excluded from synchronized input.
    /// </summary>
    public required bool Excluded { get; init; }
}
