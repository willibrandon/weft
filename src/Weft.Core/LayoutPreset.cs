namespace Weft.Core;

/// <summary>
/// A named arrangement that a layout tree can be rebuilt into.
/// </summary>
public enum LayoutPreset
{
    /// <summary>
    /// Every block side by side with equal widths.
    /// </summary>
    EvenHorizontal,

    /// <summary>
    /// Every block stacked with equal heights.
    /// </summary>
    EvenVertical,

    /// <summary>
    /// One main block on the left and the rest stacked on the right.
    /// </summary>
    MainVertical,

    /// <summary>
    /// One main block on top and the rest side by side below.
    /// </summary>
    MainHorizontal,

    /// <summary>
    /// A grid with as many rows as columns as possible.
    /// </summary>
    Tiled
}
