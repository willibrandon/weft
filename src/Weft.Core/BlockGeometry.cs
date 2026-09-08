namespace Weft.Core;

/// <summary>
/// The computed placement of one tiled block within a tab.
/// </summary>
/// <param name="Block">The block.</param>
/// <param name="Bounds">The cell rectangle the block occupies, including any frame.</param>
public readonly record struct BlockGeometry(BlockId Block, LayoutRect Bounds);
