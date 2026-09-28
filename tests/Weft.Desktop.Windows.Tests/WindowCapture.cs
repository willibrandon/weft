using Microsoft.Graphics.Canvas;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Security.Authorization.AppCapabilityAccess;
using Windows.UI;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// The composed pixels of a window's client area, as the compositor presents them.
/// </summary>
/// <remarks>
/// Windows Graphics Capture copies the window's own composition, including the terminal's DirectX canvas,
/// whether or not the window is in front. Pixels are addressed from the client area's top-left corner.
/// </remarks>
internal sealed class WindowCapture
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
    private readonly byte[] _pixels;
    private readonly int _stride;
    private readonly int _left;
    private readonly int _top;

    private WindowCapture(byte[] pixels, int stride, int left, int top, int width, int height)
    {
        _pixels = pixels;
        _stride = stride;
        _left = left;
        _top = top;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the client area's width in physical pixels.
    /// </summary>
    internal int Width { get; }

    /// <summary>
    /// Gets the client area's height in physical pixels.
    /// </summary>
    internal int Height { get; }

    /// <summary>
    /// Captures one composed frame of a window.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="id">The window's identifier.</param>
    /// <param name="cancellationToken">Cancels the wait for a frame.</param>
    /// <returns>The capture.</returns>
    internal static async Task<WindowCapture> TakeAsync(nint window, ulong id, CancellationToken cancellationToken)
    {
        AppCapabilityAccessStatus programmatic = await GraphicsCaptureAccess
            .RequestAccessAsync(GraphicsCaptureAccessKind.Programmatic).AsTask(cancellationToken).ConfigureAwait(true);
        AppCapabilityAccessStatus borderless = await GraphicsCaptureAccess
            .RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask(cancellationToken).ConfigureAwait(true);
        GraphicsCaptureItem item = GraphicsCaptureItem.TryCreateFromWindowId(new WindowId(id))
            ?? throw new AssertFailedException("The window cannot be captured. Supported: "
                + GraphicsCaptureSession.IsSupported()
                + ", programmatic access: " + programmatic + ", borderless access: " + borderless + ".");
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(CanvasDevice.GetSharedDevice(),
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using GraphicsCaptureSession session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        // Without borderless access, Windows outlines the window on screen while it is captured, outside the pixels.
        session.IsBorderRequired = borderless != AppCapabilityAccessStatus.Allowed;
        // Each frame is copied out where it arrives, so no frame outlives its handler.
        var arrived = new TaskCompletionSource<(byte[] Pixels, int Width, int Height)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pool.FrameArrived += (sender, _) =>
        {
            using Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame is not null && !arrived.Task.IsCompleted)
            {
                using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(CanvasDevice.GetSharedDevice(),
                    frame.Surface);
                _ = arrived.TrySetResult((bitmap.GetPixelBytes(), (int)bitmap.SizeInPixels.Width,
                    (int)bitmap.SizeInPixels.Height));
            }
        };
        session.StartCapture();
        (byte[] pixels, int width, int height) = await arrived.Task.WaitAsync(s_timeout, cancellationToken)
            .ConfigureAwait(true);
        int stride = width * 4;

        // The frame covers the window's visible bounds; the client area sits inside them.
        Assert.AreEqual(0, NativeMethods.DwmGetWindowAttribute(window, NativeMethods.ExtendedFrameBounds,
            out NativeRect bounds, Marshal.SizeOf<NativeRect>()), "The window's frame bounds are unavailable.");
        var origin = new NativePoint();
        Assert.IsTrue(NativeMethods.ClientToScreen(window, ref origin), "The client area could not be located.");
        int left = origin.X - bounds.Left;
        int top = origin.Y - bounds.Top;
        return new WindowCapture(pixels, stride, left, top, width - left, height - top);
    }

    /// <summary>
    /// Gets a pixel's color.
    /// </summary>
    /// <param name="x">The column from the client area's left edge.</param>
    /// <param name="y">The row from the client area's top edge.</param>
    /// <returns>The opaque color.</returns>
    internal Color At(int x, int y)
    {
        Assert.IsTrue(x >= 0 && y >= 0 && x < Width && y < Height, "Pixel " + x + "," + y + " is outside the window.");
        int offset = Offset(x, y);
        return Color.FromArgb(255, _pixels[offset + 2], _pixels[offset + 1], _pixels[offset]);
    }

    /// <summary>
    /// Lists the rows inside a rectangle whose pixels differ from another capture.
    /// </summary>
    /// <param name="other">The other capture of the same window size.</param>
    /// <param name="left">The rectangle's left edge.</param>
    /// <param name="top">The rectangle's top edge.</param>
    /// <param name="right">The rectangle's right edge, exclusive.</param>
    /// <param name="bottom">The rectangle's bottom edge, exclusive.</param>
    /// <returns>The differing rows.</returns>
    internal IReadOnlyList<int> ChangedRows(WindowCapture other, int left, int top, int right, int bottom)
    {
        ArgumentNullException.ThrowIfNull(other);
        Assert.AreEqual(Width, other.Width, "The window width changed between captures.");
        Assert.AreEqual(Height, other.Height, "The window height changed between captures.");
        int first = Math.Max(0, left);
        int length = (Math.Min(Width, right) - first) * 4;
        var rows = new List<int>();
        for (int y = Math.Max(0, top); y < Math.Min(Height, bottom); y++)
        {
            if (!_pixels.AsSpan(Offset(first, y), length)
                .SequenceEqual(other._pixels.AsSpan(other.Offset(first, y), length)))
            {
                rows.Add(y);
            }
        }

        return rows;
    }

    private int Offset(int x, int y)
    {
        return ((y + _top) * _stride) + ((x + _left) * 4);
    }
}
