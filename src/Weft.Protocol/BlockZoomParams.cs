namespace Weft.Protocol;

/// <summary>
/// Parameters for block.zoom.
/// </summary>
public sealed class BlockZoomParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets whether to zoom; null toggles.
    /// </summary>
    public bool? Zoom { get; init; }
}
