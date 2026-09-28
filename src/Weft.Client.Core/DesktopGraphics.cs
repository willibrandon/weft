using Hex1b;
using Hex1b.Automation;
using System.Runtime.CompilerServices;

namespace Weft.Client;

/// <summary>
/// Projects terminal raster placements into bounded native drawing instructions.
/// </summary>
internal static class DesktopGraphics
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private const int MaximumPlacements = 16_384;
    private static readonly DesktopGraphicsContext s_context = new();
    private static readonly ConditionalWeakTable<KgpImageData, DesktopRaster> s_rasters = [];

    /// <summary>
    /// Captures visible images while limiting both raster payload and decoded allocation.
    /// </summary>
    /// <param name="snapshot">The immutable terminal snapshot.</param>
    /// <param name="top">The first displayed snapshot row.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="sixel">The terminal's bounded Sixel projection cache.</param>
    /// <returns>The visible native image instructions.</returns>
    internal static IReadOnlyList<DesktopImage> Capture(Hex1bTerminalSnapshot snapshot, int top, int height, DesktopSixel sixel)
    {
        List<DesktopImage> result = [];
        int remaining = MaximumBytes;
        HashSet<KgpImageData> retained = [];
        foreach (KgpPlacement placement in snapshot.KgpPlacements.OrderBy(value => value.ZIndex).ThenBy(value => value.ImageId)
            .ThenBy(value => value.PlacementId).Where(value => value.Row + value.DisplayRows > top
            && value.Row < top + height && result.Count < MaximumPlacements
            && snapshot.KgpImages.TryGetValue(value.ImageId, out KgpImageData? image)
            && image.Width > 0 && image.Height > 0 && (retained.Contains(image) || (long)image.Width * image.Height * 4 <= remaining)))
        {
            KgpImageData image = snapshot.KgpImages[placement.ImageId];
            if (!image.IsDataSizeValid())
            {
                continue;
            }
            DesktopRaster raster = s_rasters.GetValue(image, value => s_context.Resolve(value, placement));
            byte[] data = raster.Data;
            if (!retained.Contains(image) && data.Length > remaining)
            {
                continue;
            }

            double sourceWidth = Math.Min(placement.SourceWidth == 0 ? image.Width : placement.SourceWidth, (double)image.Width - placement.SourceX);
            double sourceHeight = Math.Min(placement.SourceHeight == 0 ? image.Height : placement.SourceHeight, (double)image.Height - placement.SourceY);
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                continue;
            }

            double clipX = placement.Column + ((double)placement.CellOffsetX / Math.Max(1, snapshot.CellPixelWidth));
            double clipY = placement.Row - top + ((double)placement.CellOffsetY / Math.Max(1, snapshot.CellPixelHeight));
            bool nativeSize = s_context.UsesNativeSize(placement);
            double clipWidth = nativeSize ? sourceWidth / Math.Max(1, snapshot.CellPixelWidth)
                : placement.DisplayColumns - ((double)placement.CellOffsetX / Math.Max(1, snapshot.CellPixelWidth));
            double clipHeight = nativeSize ? sourceHeight / Math.Max(1, snapshot.CellPixelHeight)
                : placement.DisplayRows - ((double)placement.CellOffsetY / Math.Max(1, snapshot.CellPixelHeight));
            if (clipWidth <= 0 || clipHeight <= 0)
            {
                continue;
            }
            double width = image.Width * clipWidth / sourceWidth;
            double imageHeight = image.Height * clipHeight / sourceHeight;
            double x = clipX - (placement.SourceX * clipWidth / sourceWidth);
            double y = clipY - (placement.SourceY * clipHeight / sourceHeight);
            if (placement.RenderGeometry is { } geometry)
            {
                clipX = placement.Column + geometry.ClipOffsetXInCells;
                clipY = placement.Row - top + geometry.ClipOffsetYInCells;
                clipWidth = geometry.ClipWidthInCells;
                clipHeight = geometry.ClipHeightInCells;
                x = placement.Column + geometry.ImageOffsetXInCells;
                y = placement.Row - top + geometry.ImageOffsetYInCells;
                width = geometry.ImageWidthInCells;
                imageHeight = geometry.ImageHeightInCells;
            }

            result.Add(new DesktopImage(raster.Key, data, raster.Format, (int)image.Width, (int)image.Height,
                x, y, width, imageHeight, clipX, clipY, clipWidth, clipHeight, placement.ZIndex));
            if (retained.Add(image))
            {
                remaining -= Math.Max(data.Length, (int)((long)image.Width * image.Height * 4));
            }
        }

        if (sixel.Capture(snapshot, top, height, remaining) is { } plane)
        {
            result.Add(plane);
        }

        return result;
    }
}
