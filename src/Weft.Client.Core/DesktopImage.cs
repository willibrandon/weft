using System.Text.Json.Serialization;

namespace Weft.Client;

/// <summary>
/// Carries a bounded raster and its visible geometry in terminal-cell coordinates.
/// </summary>
/// <param name="Key">The content digest used by native image caches.</param>
/// <param name="Data">PNG, RGB, or RGBA pixels, according to the format.</param>
/// <param name="Format">The raster format: 24 for RGB, 32 for RGBA, or 100 for PNG.</param>
/// <param name="PixelWidth">The decoded pixel width.</param>
/// <param name="PixelHeight">The decoded pixel height.</param>
/// <param name="X">The complete image's left edge in cells.</param>
/// <param name="Y">The complete image's top edge in cells.</param>
/// <param name="Width">The complete image's width in cells.</param>
/// <param name="Height">The complete image's height in cells.</param>
/// <param name="ClipX">The visible left edge in cells.</param>
/// <param name="ClipY">The visible top edge in cells.</param>
/// <param name="ClipWidth">The visible width in cells.</param>
/// <param name="ClipHeight">The visible height in cells.</param>
/// <param name="Layer">The image's stacking order relative to text.</param>
public sealed record DesktopImage(string Key, [property: JsonIgnore] ReadOnlyMemory<byte> Data, int Format, int PixelWidth, int PixelHeight,
    double X, double Y, double Width, double Height, double ClipX, double ClipY, double ClipWidth, double ClipHeight, int Layer);
