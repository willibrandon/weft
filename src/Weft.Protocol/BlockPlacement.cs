namespace Weft.Protocol;

/// <summary>
/// Where a block sits within its tab's authoritative area.
/// </summary>
public sealed class BlockPlacement
{
    /// <summary>
    /// Gets the block id.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the leftmost column.
    /// </summary>
    public required int X { get; init; }

    /// <summary>
    /// Gets the topmost row.
    /// </summary>
    public required int Y { get; init; }

    /// <summary>
    /// Gets the width in columns, including any frame.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the height in rows, including any frame.
    /// </summary>
    public required int Height { get; init; }
}
