namespace Weft.Core;

/// <summary>
/// Sizing rules that a layout tree applies when computing geometry.
/// </summary>
/// <param name="Spacing">Cells left between sibling cells, one for tmux-style separators and zero when blocks draw their own frames.</param>
/// <param name="MinimumWidth">The smallest width a leaf may be given.</param>
/// <param name="MinimumHeight">The smallest height a leaf may be given.</param>
public readonly record struct LayoutOptions(int Spacing, int MinimumWidth, int MinimumHeight)
{
    /// <summary>
    /// Options for blocks that draw a one-cell frame on every side.
    /// </summary>
    public static LayoutOptions Framed => new(0, 3, 3);

    /// <summary>
    /// Options for tmux-style layouts with a one-cell separator between blocks.
    /// </summary>
    public static LayoutOptions Separated => new(1, 1, 1);

    /// <summary>
    /// Gets the minimum size of a leaf along an orientation.
    /// </summary>
    /// <param name="orientation">The orientation.</param>
    /// <returns>The minimum width for left-right, otherwise the minimum height.</returns>
    public int Minimum(SplitOrientation orientation) =>
        orientation == SplitOrientation.LeftRight ? MinimumWidth : MinimumHeight;
}
