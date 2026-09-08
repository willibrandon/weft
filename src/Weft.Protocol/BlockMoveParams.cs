namespace Weft.Protocol;

/// <summary>
/// Parameters for block.move, which repositions or resizes a floating block.
/// </summary>
public sealed class BlockMoveParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the leftmost column; null keeps the current value.
    /// </summary>
    public int? X { get; init; }

    /// <summary>
    /// Gets the topmost row; null keeps the current value.
    /// </summary>
    public int? Y { get; init; }

    /// <summary>
    /// Gets the width; null keeps the current value.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Gets the height; null keeps the current value.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Gets a column delta applied after the absolute values.
    /// </summary>
    public int DeltaX { get; init; }

    /// <summary>
    /// Gets a row delta applied after the absolute values.
    /// </summary>
    public int DeltaY { get; init; }
}
