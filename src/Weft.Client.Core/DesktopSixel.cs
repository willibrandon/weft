using Hex1b;
using Hex1b.Automation;
using Hex1b.Surfaces;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Weft.Client;

/// <summary>
/// Composites the visible Sixel plane without charging hidden historical rasters to the frame budget.
/// </summary>
internal sealed class DesktopSixel
{
    private readonly Dictionary<string, DesktopRasterSize> _sizes = [with(StringComparer.Ordinal)];

    /// <summary>
    /// Resolves newer pixels first and stops when they cover the viewport completely.
    /// </summary>
    /// <param name="snapshot">The terminal's immutable graphics state.</param>
    /// <param name="top">The first displayed row.</param>
    /// <param name="height">The displayed row count.</param>
    /// <param name="budget">The remaining decoded byte budget.</param>
    /// <returns>One visible raster, or null when there are no painted pixels.</returns>
    internal DesktopImage? Capture(Hex1bTerminalSnapshot snapshot, int top, int height, int budget)
    {
        var retained = snapshot.SixelImages.Keys.Select(Convert.ToHexString).ToHashSet(StringComparer.Ordinal);
        foreach (string key in _sizes.Keys.Where(key => !retained.Contains(key)).ToArray())
        {
            _ = _sizes.Remove(key);
        }
        int cellWidth = Math.Max(1, snapshot.CellPixelWidth);
        int cellHeight = Math.Max(1, snapshot.CellPixelHeight);
        int width = snapshot.Width * cellWidth;
        int rows = height * cellHeight;
        if (snapshot.SixelPlacements.Count == 0 || (long)width * rows * 4 > budget)
        {
            return null;
        }

        byte[]? canvas = null;
        int[] uncoveredLeft = new int[rows];
        int[] uncoveredRight = new int[rows];
        Array.Fill(uncoveredRight, width);
        int opaque = 0;
        int left = width;
        int right = 0;
        int first = rows;
        int last = 0;
        string? identity = null;
        foreach (SixelPlacement placement in snapshot.SixelPlacements.OrderByDescending(value => value.Sequence)
            .Where(value => value.HasVisiblePaintedCells && value.PaintedBottom >= top && value.PaintedTop < top + height))
        {
            double scaleX = cellWidth / placement.Image.CellMetrics.SafeWidth;
            double scaleY = cellHeight / placement.Image.CellMetrics.SafeHeight;
            // Read the painted crop directly from damage-masked row spans. Materializing
            // a second cropped image adds a full raster copy to every animation frame.
            string key = Convert.ToHexString(placement.Image.ContentHash);
            _ = _sizes.TryGetValue(key, out DesktopRasterSize? size);
            (int sourceLeft, int sourceTop, int sourceWidth, int sourceHeight) = Crop(placement,
                size?.Width ?? int.MaxValue, size?.Height ?? int.MaxValue);
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                continue;
            }
            int originX = placement.PaintedLeft * cellWidth;
            int originY = (placement.PaintedTop - top) * cellHeight;
            int endX = Math.Min(width, originX + (int)Math.Ceiling(sourceWidth * scaleX));
            int endY = Math.Min(rows, originY + (int)Math.Ceiling(sourceHeight * scaleY));
            // Do not materialize old raster buffers when newer pixels cover their entire visible crop.
            if (Covered(uncoveredLeft, uncoveredRight, Math.Max(0, originX), endX, Math.Max(0, originY), endY))
            {
                continue;
            }
            SixelPixelBuffer? pixels = placement.GetVisiblePixels();
            if (pixels is null)
            {
                continue;
            }
            _sizes[key] = new DesktopRasterSize(pixels.Width, pixels.Height);
            (sourceLeft, sourceTop, sourceWidth, sourceHeight) = Crop(placement, pixels.Width, pixels.Height);
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                continue;
            }
            endX = Math.Min(width, originX + (int)Math.Ceiling(sourceWidth * scaleX));
            endY = Math.Min(rows, originY + (int)Math.Ceiling(sourceHeight * scaleY));
            int startX = Math.Max(0, originX);
            int startY = Math.Max(0, originY);
            if (canvas is null && scaleX == 1 && scaleY == 1 && endX > startX && endY > startY
                && CopyOpaque(pixels, sourceLeft + startX - originX, sourceTop + startY - originY,
                    endX - startX, endY - startY, width, rows, startX, startY) is { } front)
            {
                canvas = front;
                left = startX;
                right = endX;
                first = startY;
                last = endY;
                opaque = (endX - startX) * (endY - startY);
                for (int y = startY; y < endY; y++)
                {
                    uncoveredLeft[y] = startX == 0 ? endX : 0;
                    uncoveredRight[y] = endX == width ? startX : width;
                }
                if (opaque == width * rows)
                {
                    // The source already has a content identity covering pixels, palette, and cell metrics.
                    // A fully covering crop can reuse it instead of hashing the complete output again.
                    identity = string.Create(CultureInfo.InvariantCulture,
                        $"sixel:{key}:{sourceLeft + startX - originX}:{sourceTop + startY - originY}:{width}:{rows}");
                    break;
                }
                continue;
            }
            canvas ??= new byte[width * rows * 4];
            for (int y = Math.Max(0, originY); y < endY; y++)
            {
                ReadOnlySpan<Rgba32> source = pixels.GetRow(sourceTop + Math.Min(sourceHeight - 1, (int)((y - originY) / scaleY)))
                    .Slice(sourceLeft, sourceWidth);
                int rowOffset = y * width * 4;
                for (int x = Math.Max(0, originX); x < endX; x++)
                {
                    int offset = rowOffset + (x * 4);
                    if (canvas[offset + 3] == 255)
                    {
                        continue;
                    }

                    Rgba32 pixel = source[scaleX == 1 ? x - originX : Math.Min(sourceWidth - 1, (int)((x - originX) / scaleX))];
                    if (pixel.A == 0)
                    {
                        continue;
                    }

                    int frontAlpha = canvas[offset + 3];
                    if (frontAlpha == 0)
                    {
                        canvas[offset] = pixel.R;
                        canvas[offset + 1] = pixel.G;
                        canvas[offset + 2] = pixel.B;
                        canvas[offset + 3] = pixel.A;
                    }
                    else
                    {
                        int backAlpha = ((pixel.A * (255 - frontAlpha)) + 127) / 255;
                        int alpha = frontAlpha + backAlpha;
                        canvas[offset] = (byte)(((canvas[offset] * frontAlpha) + (pixel.R * backAlpha)) / alpha);
                        canvas[offset + 1] = (byte)(((canvas[offset + 1] * frontAlpha) + (pixel.G * backAlpha)) / alpha);
                        canvas[offset + 2] = (byte)(((canvas[offset + 2] * frontAlpha) + (pixel.B * backAlpha)) / alpha);
                        canvas[offset + 3] = (byte)alpha;
                    }
                    opaque += canvas[offset + 3] == 255 ? 1 : 0;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x + 1);
                    first = Math.Min(first, y);
                    last = Math.Max(last, y + 1);
                }
                while (uncoveredLeft[y] < width && canvas[rowOffset + (uncoveredLeft[y] * 4) + 3] == 255)
                {
                    uncoveredLeft[y]++;
                }
                while (uncoveredRight[y] > uncoveredLeft[y] && canvas[rowOffset + ((uncoveredRight[y] - 1) * 4) + 3] == 255)
                {
                    uncoveredRight[y]--;
                }
            }

            if (opaque == width * rows)
            {
                break;
            }
        }

        if (canvas is null || right <= left || last <= first)
        {
            return null;
        }

        int pixelWidth = right - left;
        int pixelHeight = last - first;
        byte[] data = canvas;
        if (pixelWidth != width || pixelHeight != rows)
        {
            data = new byte[pixelWidth * pixelHeight * 4];
            for (int row = 0; row < pixelHeight; row++)
            {
                Buffer.BlockCopy(canvas, (((first + row) * width) + left) * 4, data, row * pixelWidth * 4, pixelWidth * 4);
            }
        }

        double xCells = (double)left / cellWidth;
        double yCells = (double)first / cellHeight;
        double widthCells = (double)pixelWidth / cellWidth;
        double heightCells = (double)pixelHeight / cellHeight;
        return new DesktopImage(identity ?? Convert.ToHexString(SHA256.HashData(data)), data, 32, pixelWidth, pixelHeight,
            xCells, yCells, widthCells, heightCells, xCells, yCells, widthCells, heightCells, 0);
    }

    private static byte[]? CopyOpaque(SixelPixelBuffer pixels, int sourceX, int sourceY, int width, int height,
        int stride, int rows, int x, int y)
    {
        // An opaque, unscaled front plane needs neither blending nor per-pixel bounds tracking.
        // Validate all rows before writing so transparent images use the general compositor.
        for (int row = 0; row < height; row++)
        {
            if (!IsOpaque(pixels.GetRow(sourceY + row).Slice(sourceX, width)))
            {
                return null;
            }
        }
        // Every opaque pixel is overwritten. Clear only the surrounding transparent
        // area, avoiding a redundant full-plane fill during opaque animation.
        byte[] canvas = GC.AllocateUninitializedArray<byte>(stride * rows * 4);
        canvas.AsSpan(0, y * stride * 4).Clear();
        canvas.AsSpan((y + height) * stride * 4).Clear();
        for (int row = 0; row < height; row++)
        {
            int offset = (y + row) * stride * 4;
            canvas.AsSpan(offset, x * 4).Clear();
            canvas.AsSpan(offset + ((x + width) * 4), (stride - x - width) * 4).Clear();
            MemoryMarshal.AsBytes(pixels.GetRow(sourceY + row).Slice(sourceX, width))
                .CopyTo(canvas.AsSpan(offset + (x * 4), width * 4));
        }
        return canvas;
    }

    private static bool IsOpaque(ReadOnlySpan<Rgba32> pixels)
    {
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<Rgba32, uint>(pixels);
        var mask = new Vector<uint>(BitConverter.IsLittleEndian ? 0xFF000000u : 0xFFu);
        int index = 0;
        for (; index <= words.Length - Vector<uint>.Count; index += Vector<uint>.Count)
        {
            if (!Vector.EqualsAll(new Vector<uint>(words[index..]) & mask, mask))
            {
                return false;
            }
        }
        for (; index < pixels.Length; index++)
        {
            if (pixels[index].A != 255)
            {
                return false;
            }
        }
        return true;
    }

    private static (int Left, int Top, int Width, int Height) Crop(SixelPlacement placement, int imageWidth, int imageHeight)
    {
        int left = Math.Clamp((int)Math.Floor(placement.PaintedColumnOffset * placement.Image.CellMetrics.SafeWidth), 0, imageWidth);
        int top = Math.Clamp((int)Math.Floor(placement.PaintedRowOffset * placement.Image.CellMetrics.SafeHeight), 0, imageHeight);
        int right = Math.Clamp((int)Math.Ceiling((placement.PaintedColumnOffset + placement.PaintedColumnCount)
            * placement.Image.CellMetrics.SafeWidth), 0, imageWidth);
        int bottom = Math.Clamp((int)Math.Ceiling((placement.PaintedRowOffset + placement.PaintedRowCount)
            * placement.Image.CellMetrics.SafeHeight), 0, imageHeight);
        return (left, top, right - left, bottom - top);
    }

    private static bool Covered(int[] left, int[] right, int startX, int endX, int startY, int endY)
    {
        if (endX <= startX)
        {
            return true;
        }
        for (int row = startY; row < endY; row++)
        {
            if (startX < right[row] && endX > left[row])
            {
                return false;
            }
        }
        return true;
    }
}
