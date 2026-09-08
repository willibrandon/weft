namespace Weft.Core;

/// <summary>
/// A rectangle in cell coordinates with the origin at the top left.
/// </summary>
/// <param name="X">The leftmost column.</param>
/// <param name="Y">The topmost row.</param>
/// <param name="Width">The width in columns.</param>
/// <param name="Height">The height in rows.</param>
public readonly record struct LayoutRect(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Gets the column just past the right edge.
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// Gets the row just past the bottom edge.
    /// </summary>
    public int Bottom => Y + Height;
}
