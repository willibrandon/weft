using Microsoft.Graphics.Canvas;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Weft.Client;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;

namespace Weft.Desktop.Windows;

/// <summary>
/// Keeps only current-frame rasters, with hard limits on decoded bytes and image count.
/// </summary>
/// <remarks>
/// Pixels arrive in memory from the shared core; terminal output cannot name a file for the app to open.
/// PNG textures decode asynchronously and redraw the surface when they are ready.
/// </remarks>
internal sealed partial class TerminalImages : IDisposable
{
    private const int ByteLimit = 32 * 1024 * 1024;
    private const int CountLimit = 256;
    private readonly Dictionary<string, (CanvasBitmap Bitmap, int Cost, ulong Used)> _cache = [with(StringComparer.Ordinal)];
    private readonly HashSet<string> _decoding = [with(StringComparer.Ordinal)];
    private readonly Action _invalidate;
    private ulong _clock;
    private bool _disposed;

    /// <summary>
    /// Creates an empty cache.
    /// </summary>
    /// <param name="invalidate">Redraws the surface after an asynchronous decode completes.</param>
    internal TerminalImages(Action invalidate)
    {
        _invalidate = invalidate;
    }

    /// <summary>
    /// Gets the accounted decoded bytes, excluding the compositor's own storage.
    /// </summary>
    internal int RetainedBytes { get; private set; }

    /// <summary>
    /// Gets the number of retained images.
    /// </summary>
    internal int Count => _cache.Count;

    /// <summary>
    /// Gets the cache key for a texture, which includes its format and dimensions.
    /// </summary>
    /// <param name="texture">The texture.</param>
    /// <returns>The key.</returns>
    internal static string KeyOf(DesktopTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return string.Create(CultureInfo.InvariantCulture,
            $"{texture.Key}:{texture.Format}:{texture.PixelWidth}x{texture.PixelHeight}");
    }

    /// <summary>
    /// Gets the cache key a placement refers to.
    /// </summary>
    /// <param name="image">The placement.</param>
    /// <returns>The key.</returns>
    internal static string KeyOf(DesktopImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return string.Create(CultureInfo.InvariantCulture,
            $"{image.Key}:{image.Format}:{image.PixelWidth}x{image.PixelHeight}");
    }

    /// <summary>
    /// Releases images absent from the current frame; animation must not accumulate old frames.
    /// </summary>
    /// <param name="keys">The keys referenced by the current frame.</param>
    internal void Retain(IReadOnlySet<string> keys)
    {
        foreach (string key in _cache.Keys.Where(key => !keys.Contains(key)).ToArray())
        {
            Remove(key);
        }
    }

    /// <summary>
    /// Gets a drawable bitmap, creating it when needed; PNG data is decoded later and returns null meanwhile.
    /// </summary>
    /// <param name="device">The drawing device.</param>
    /// <param name="texture">The texture.</param>
    /// <returns>The bitmap, or null when it is invalid, too large, or still decoding.</returns>
    internal CanvasBitmap? Get(CanvasDevice device, DesktopTexture texture)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(texture);
        string key = KeyOf(texture);
        _clock++;
        if (_cache.TryGetValue(key, out (CanvasBitmap Bitmap, int Cost, ulong Used) existing))
        {
            _cache[key] = existing with { Used = _clock };
            return existing.Bitmap;
        }

        if (texture.PixelWidth is <= 0 or > 4096 || texture.PixelHeight is <= 0 or > 4096
            || texture.PixelWidth * texture.PixelHeight > 4_194_304 || texture.Data.Length > 16 * 1024 * 1024)
        {
            return null;
        }

        if (texture.Format == 100)
        {
            if (_decoding.Add(key))
            {
                _ = DecodeAsync(device, key, texture);
            }

            return null;
        }

        byte[]? pixels = Premultiply(texture);
        if (pixels is null)
        {
            return null;
        }

        var bitmap = CanvasBitmap.CreateFromBytes(device, pixels, texture.PixelWidth, texture.PixelHeight,
            DirectXPixelFormat.R8G8B8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        try
        {
            Add(key, bitmap, pixels.Length);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Releases every image, for example after the drawing device is lost.
    /// </summary>
    internal void Clear()
    {
        foreach (string key in _cache.Keys.ToArray())
        {
            Remove(key);
        }

        _decoding.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        Clear();
    }

    private static byte[]? Premultiply(DesktopTexture texture)
    {
        int count = texture.PixelWidth * texture.PixelHeight;
        ReadOnlySpan<byte> source = texture.Data.Span;
        int channels = texture.Format == 24 ? 3 : texture.Format == 32 ? 4 : 0;
        if (channels == 0 || source.Length != count * channels)
        {
            return null;
        }

        byte[] pixels = new byte[count * 4];
        for (int index = 0; index < count; index++)
        {
            int from = index * channels;
            int to = index * 4;
            int alpha = channels == 4 ? source[from + 3] : 255;
            pixels[to] = (byte)(source[from] * alpha / 255);
            pixels[to + 1] = (byte)(source[from + 1] * alpha / 255);
            pixels[to + 2] = (byte)(source[from + 2] * alpha / 255);
            pixels[to + 3] = (byte)alpha;
        }

        return pixels;
    }

    private async Task DecodeAsync(CanvasDevice device, string key, DesktopTexture texture)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            _ = await stream.WriteAsync(texture.Data.ToArray().AsBuffer());
            stream.Seek(0);
            CanvasBitmap bitmap = await CanvasBitmap.LoadAsync(device, stream, 96, CanvasAlphaMode.Premultiplied);
            bool matches = bitmap.SizeInPixels.Width == texture.PixelWidth && bitmap.SizeInPixels.Height == texture.PixelHeight;
            if (_disposed || !matches || !_decoding.Contains(key))
            {
                bitmap.Dispose();
                return;
            }

            Add(key, bitmap, texture.PixelWidth * texture.PixelHeight * 4);
            _invalidate();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or FormatException)
        {
            ClientLog.Debug("A terminal image could not be decoded: " + exception.Message);
        }
        finally
        {
            _ = _decoding.Remove(key);
        }
    }

    private void Add(string key, CanvasBitmap bitmap, int cost)
    {
        if (cost > ByteLimit)
        {
            // A single oversized image stays drawable for this frame without displacing the cache.
            _cache[key] = (bitmap, 0, _clock);
            return;
        }

        while (_cache.Count >= CountLimit || RetainedBytes + cost > ByteLimit)
        {
            string? oldest = _cache.MinBy(entry => entry.Value.Used).Key;
            if (oldest is null)
            {
                break;
            }

            Remove(oldest);
        }

        _cache[key] = (bitmap, cost, _clock);
        RetainedBytes += cost;
    }

    private void Remove(string key)
    {
        if (_cache.Remove(key, out (CanvasBitmap Bitmap, int Cost, ulong Used) entry))
        {
            RetainedBytes -= entry.Cost;
            entry.Bitmap.Dispose();
        }
    }
}
