namespace Weft.Protocol;

/// <summary>
/// Parameters for block.float.
/// </summary>
public sealed class BlockFloatParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the leftmost column; null centers the block.
    /// </summary>
    public int? X { get; init; }

    /// <summary>
    /// Gets the topmost row; null centers the block.
    /// </summary>
    public int? Y { get; init; }

    /// <summary>
    /// Gets the width including frame; null uses most of the area.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Gets the height including frame; null uses most of the area.
    /// </summary>
    public int? Height { get; init; }
}
