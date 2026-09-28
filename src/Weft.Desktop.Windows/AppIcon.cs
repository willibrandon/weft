using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Weft.Desktop.Windows;

/// <summary>
/// Draws the original woven W mark for the window, the executable, and packaged logos.
/// </summary>
/// <remarks>
/// This is the Mac artwork drawn with the same coordinates on a 1024-unit square, with its vertical axis
/// flipped for Direct2D. Rendering runs without constructing an application or a window.
/// </remarks>
internal static class AppIcon
{
    private static readonly Color s_thread = Color.FromArgb(255, 117, 166, 179);
    private static readonly Color s_ivory = Color.FromArgb(255, 227, 232, 227);
    private static readonly Color s_deep = Color.FromArgb(255, 14, 26, 33);
    private static readonly Color s_raised = Color.FromArgb(255, 33, 51, 61);

    /// <summary>
    /// Writes the icon file and package logos into a directory.
    /// </summary>
    /// <param name="directory">The destination directory.</param>
    /// <returns>A task that completes when every file is written.</returns>
    internal static async Task RenderAsync(string directory)
    {
        _ = Directory.CreateDirectory(directory);
        using var device = new CanvasDevice();
        int[] iconSizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var images = new List<(int Size, byte[] Png)>();
        foreach (int size in iconSizes)
        {
            images.Add((size, await PngAsync(device, size, size, size).ConfigureAwait(true)));
        }

        await File.WriteAllBytesAsync(Path.Join(directory, "AppIcon.ico"), Icon(images)).ConfigureAwait(true);
        foreach (int size in (int[])[16, 24, 32, 48, 256])
        {
            byte[] png = await PngAsync(device, size, size, size).ConfigureAwait(true);
            await File.WriteAllBytesAsync(Path.Join(directory, $"Square44x44Logo.targetsize-{size}.png"), png).ConfigureAwait(true);
            await File.WriteAllBytesAsync(Path.Join(directory, $"Square44x44Logo.targetsize-{size}_altform-unplated.png"), png)
                .ConfigureAwait(true);
        }

        (string Name, int Width, int Height, int Mark)[] logos =
        [
            ("Square44x44Logo.scale-100.png", 44, 44, 44),
            ("Square44x44Logo.scale-200.png", 88, 88, 88),
            ("Square150x150Logo.scale-100.png", 150, 150, 110),
            ("Square150x150Logo.scale-200.png", 300, 300, 220),
            ("Wide310x150Logo.scale-100.png", 310, 150, 110),
            ("Wide310x150Logo.scale-200.png", 620, 300, 220),
            ("StoreLogo.scale-100.png", 50, 50, 50),
            ("StoreLogo.scale-200.png", 100, 100, 100)
        ];
        foreach ((string name, int width, int height, int mark) in logos)
        {
            await File.WriteAllBytesAsync(Path.Join(directory, name), await PngAsync(device, width, height, mark).ConfigureAwait(true))
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Draws the mark into a session scaled to a square of the given size.
    /// </summary>
    /// <param name="session">The drawing session.</param>
    /// <param name="size">The square's size in pixels.</param>
    internal static void Draw(CanvasDrawingSession session, float size)
    {
        ArgumentNullException.ThrowIfNull(session);
        Matrix3x2 transform = session.Transform;
        session.Transform = Matrix3x2.CreateScale(size / 1024) * transform;
        using var tile = CanvasGeometry.CreateRoundedRectangle(session, new Rect(96, 96, 832, 832), 184, 184);
        using (var shape = new CanvasCommandList(session))
        {
            using (CanvasDrawingSession shadowSession = shape.CreateDrawingSession())
            {
                shadowSession.FillGeometry(tile, Colors(0, 0, 0, 255));
            }

            using var shadow = new ShadowEffect { Source = shape, BlurAmount = 14, ShadowColor = Colors(0, 0, 0, 56) };
            session.DrawImage(shadow, new Vector2(0, 12));
        }

        using var gradient = new CanvasLinearGradientBrush(session, s_raised, s_deep)
        {
            StartPoint = new Vector2(512, 96),
            EndPoint = new Vector2(512, 928)
        };
        session.FillGeometry(tile, gradient);
        using var style = new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Round,
            EndCap = CanvasCapStyle.Round,
            LineJoin = CanvasLineJoin.Round
        };
        Stroke(session, [new(294, 512), new(730, 512)], s_thread, 44, style);
        Stroke(session, [new(276, 334), new(386, 690), new(512, 418), new(638, 690), new(748, 334)], s_ivory, 50, style);
        Stroke(session, [new(304, 512), new(354, 512)], s_thread, 44, style);
        Stroke(session, [new(670, 512), new(720, 512)], s_thread, 44, style);
        session.Transform = transform;
    }

    private static Color Colors(byte red, byte green, byte blue, byte alpha)
    {
        return Color.FromArgb(alpha, red, green, blue);
    }

    private static void Stroke(CanvasDrawingSession session, Vector2[] points, Color color, float width, CanvasStrokeStyle style)
    {
        using var builder = new CanvasPathBuilder(session);
        builder.BeginFigure(points[0]);
        foreach (Vector2 point in points.Skip(1))
        {
            builder.AddLine(point);
        }

        builder.EndFigure(CanvasFigureLoop.Open);
        using var path = CanvasGeometry.CreatePath(builder);
        session.DrawGeometry(path, color, width, style);
    }

    private static async Task<byte[]> PngAsync(CanvasDevice device, int width, int height, int mark)
    {
        using var target = new CanvasRenderTarget(device, width, height, 96);
        using (CanvasDrawingSession session = target.CreateDrawingSession())
        {
            session.Clear(Colors(0, 0, 0, 0));
            session.Transform = Matrix3x2.CreateTranslation((width - mark) / 2f, (height - mark) / 2f);
            Draw(session, mark);
        }

        using var stream = new InMemoryRandomAccessStream();
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        byte[] bytes = new byte[stream.Size];
        stream.Seek(0);
        _ = await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    /// <summary>
    /// Packs PNG images into an icon file, which Windows reads for sizes from 16 to 256 pixels.
    /// </summary>
    private static byte[] Icon(List<(int Size, byte[] Png)> images)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)images.Count);
            int offset = 6 + (16 * images.Count);
            foreach ((int size, byte[] png) in images)
            {
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(png.Length);
                writer.Write(offset);
                offset += png.Length;
            }

            foreach ((_, byte[] png) in images)
            {
                writer.Write(png);
            }
        }

        return output.ToArray();
    }
}
