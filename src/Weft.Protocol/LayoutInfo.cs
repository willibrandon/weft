namespace Weft.Protocol;

/// <summary>
/// The geometry of a tab for the session's authoritative size.
/// </summary>
public sealed class LayoutInfo
{
    /// <summary>
    /// Gets the tab id.
    /// </summary>
    public required string Tab { get; init; }

    /// <summary>
    /// Gets the width the layout was computed for.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the height the layout was computed for.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the zoomed block id, if any; when set, tiled placements are replaced by one full-area placement.
    /// </summary>
    public string? Zoomed { get; init; }

    /// <summary>
    /// Gets the tiled block placements in layout order.
    /// </summary>
    public required IReadOnlyList<BlockPlacement> Tiled { get; init; }

    /// <summary>
    /// Gets the floating block placements in stacking order, bottom first.
    /// </summary>
    public required IReadOnlyList<BlockPlacement> Floating { get; init; }

    /// <summary>
    /// Gets the serialized layout string for the tiled tree.
    /// </summary>
    public required string Serialized { get; init; }
}
