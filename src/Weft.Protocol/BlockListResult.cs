namespace Weft.Protocol;

/// <summary>
/// Result of block.list.
/// </summary>
public sealed class BlockListResult
{
    /// <summary>
    /// Gets the blocks in tab and layout order.
    /// </summary>
    public required IReadOnlyList<BlockInfo> Blocks { get; init; }
}
