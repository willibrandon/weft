using Hex1b;
using System.Globalization;
using System.Security.Cryptography;

namespace Weft.Client;

/// <summary>
/// Resolves the current animation frame through the terminal renderer's supported extension point.
/// </summary>
internal sealed class DesktopGraphicsContext : Hex1bRenderContext
{
    private static readonly KgpImageData s_geometryImage = new(1, 0, [0, 0, 0, 0], 1, 1, KgpFormat.Rgba32);

    /// <summary>
    /// Creates a renderer that projects images without writing to an output terminal.
    /// </summary>
    internal DesktopGraphicsContext() : base(theme: null)
    {
    }

    /// <summary>
    /// Copies the renderer's resolved image payload, which may differ from the stored root frame.
    /// </summary>
    /// <param name="image">The snapshot's image.</param>
    /// <param name="placement">One placement of that image.</param>
    /// <returns>The currently displayed raster.</returns>
    internal DesktopRaster Resolve(KgpImageData image, KgpPlacement placement)
    {
        KgpCellData projected = CreateKgpCellData(image, placement);
        string payload = projected.TransmitPayload!;
        int separator = payload.IndexOf(';');
        int formatStart = payload.IndexOf("f=", StringComparison.Ordinal) + 2;
        int formatEnd = payload.IndexOf(',', formatStart);
        int format = int.Parse(payload.AsSpan(formatStart, formatEnd - formatStart), CultureInfo.InvariantCulture);
        byte[] pixels = Convert.FromBase64String(payload[(separator + 1)..^2]);
        return new DesktopRaster(Convert.ToHexString(SHA256.HashData(pixels)), pixels, format);
    }

    /// <summary>
    /// Reads the renderer's placement sizing without re-encoding the texture for every sprite.
    /// </summary>
    /// <param name="placement">The immutable placement metadata.</param>
    /// <returns>Whether source pixels retain their native size.</returns>
    internal bool UsesNativeSize(KgpPlacement placement)
    {
        // Sizing belongs to the placement. A constant transparent pixel avoids
        // projecting an entire atlas just to read the public placement command.
        KgpCellData projected = CreateKgpCellData(s_geometryImage, placement);
        return !projected.Payload.AsSpan(projected.TransmitPayload!.Length).Contains(",c=", StringComparison.Ordinal);
    }
}
