using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using System.Numerics;
using System.Text;
using Weft.Client;
using Windows.Foundation;
using Windows.UI;

namespace Weft.Desktop.Windows;

/// <summary>
/// Draws terminal snapshots with explicit cell positions; the shared core owns every terminal decision.
/// </summary>
/// <remarks>
/// Glyphs found in the terminal font are batched into one glyph run per row and style, each advancing by
/// exactly one cell. Graphemes the font lacks, wide characters, and combining sequences are shaped with
/// system fallback and clipped to their own cells so the grid never moves.
/// </remarks>
internal sealed partial class TerminalRenderer : IDisposable
{
    private static readonly Color s_match = Color.FromArgb(89, 255, 214, 0);
    private readonly Dictionary<Color, CanvasSolidColorBrush> _brushes = [];
    private readonly List<CanvasGlyph> _glyphs = [];
    private CanvasDevice? _device;
    private CanvasTextFormat? _chrome;

    /// <summary>
    /// Creates a renderer for a font; the caller owns both.
    /// </summary>
    /// <param name="font">The terminal font.</param>
    /// <param name="images">The raster cache.</param>
    internal TerminalRenderer(TerminalFont font, TerminalImages images)
    {
        Font = font;
        Images = images;
    }

    /// <summary>
    /// Gets or sets the terminal font; the caller disposes replaced fonts.
    /// </summary>
    internal TerminalFont Font { get; set; }

    /// <summary>
    /// Gets the raster cache.
    /// </summary>
    internal TerminalImages Images { get; }

    /// <summary>
    /// Gets a block's content rectangle, excluding the server's pane frame.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="inset">The margin around the grid.</param>
    /// <returns>The rectangle in surface coordinates.</returns>
    internal Rect ContentRect(DesktopBlockFrame block, float inset)
    {
        ArgumentNullException.ThrowIfNull(block);
        return new Rect(inset + (block.X * Font.CellWidth), inset + (block.Y * Font.CellHeight),
            block.Width * Font.CellWidth, block.Height * Font.CellHeight);
    }

    /// <summary>
    /// Gets the rounded pane outline, which includes the title row above the content.
    /// </summary>
    /// <param name="content">The content rectangle.</param>
    /// <returns>The pane rectangle.</returns>
    internal Rect PaneRect(Rect content)
    {
        return new Rect(content.X - 5, content.Y - Font.CellHeight + 3, content.Width + 10, content.Height + Font.CellHeight + 1);
    }

    /// <summary>
    /// Gets the clickable Resume label shown while a pane inspects history.
    /// </summary>
    /// <param name="content">The content rectangle.</param>
    /// <returns>The label rectangle.</returns>
    internal Rect ResumeRect(Rect content)
    {
        return new Rect(content.Right - 135, content.Y - Font.CellHeight, 135, Font.CellHeight);
    }

    /// <summary>
    /// Draws the scene inside one invalidated region.
    /// </summary>
    /// <param name="session">The drawing session, already clipped to the region.</param>
    /// <param name="region">The invalidated region.</param>
    /// <param name="scene">The scene.</param>
    internal void Draw(CanvasDrawingSession session, Rect region, TerminalScene scene)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scene);
        if (!ReferenceEquals(_device, session.Device))
        {
            ReleaseDeviceResources();
            _device = session.Device;
        }

        session.Clear(scene.Background);
        foreach (DesktopBlockFrame block in scene.Frame.Blocks)
        {
            Rect content = ContentRect(block, scene.Inset);
            Rect pane = PaneRect(content);
            if (!Intersects(pane, region))
            {
                continue;
            }

            DrawBlock(session, region, scene, block, content);
            DrawChrome(session, scene, block, content, pane);
        }
    }

    /// <summary>
    /// Releases brushes and cached text formats tied to the drawing device.
    /// </summary>
    internal void ReleaseDeviceResources()
    {
        foreach (CanvasSolidColorBrush brush in _brushes.Values)
        {
            brush.Dispose();
        }

        _brushes.Clear();
        _chrome?.Dispose();
        _chrome = null;
        Images.Clear();
        _device = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseDeviceResources();
    }

    /// <summary>
    /// Finds the cells covered by case-insensitive matches of the search query in each visible row.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="first">The first row.</param>
    /// <param name="last">The last row.</param>
    /// <returns>The matching cell indexes.</returns>
    internal static HashSet<int> SearchCells(DesktopBlockFrame block, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(block);
        var result = new HashSet<int>();
        if (block.SearchQuery.Length == 0)
        {
            return result;
        }

        var text = new StringBuilder();
        int[] offsets = new int[block.Width + 1];
        for (int row = first; row <= last; row++)
        {
            _ = text.Clear();
            for (int column = 0; column < block.Width; column++)
            {
                offsets[column] = text.Length;
                _ = text.Append(block.Cells[(row * block.Width) + column].Text);
            }

            offsets[block.Width] = text.Length;
            string line = text.ToString();
            int start = 0;
            while (start < line.Length)
            {
                int match = line.IndexOf(block.SearchQuery, start, StringComparison.OrdinalIgnoreCase);
                if (match < 0)
                {
                    break;
                }

                int end = match + block.SearchQuery.Length;
                for (int column = 0; column < block.Width; column++)
                {
                    int length = Math.Max(1, offsets[column + 1] - offsets[column]);
                    if (offsets[column] < end && offsets[column] + length > match)
                    {
                        _ = result.Add((row * block.Width) + column);
                    }
                }

                start = Math.Max(end, match + 1);
            }
        }

        return result;
    }

    private void DrawBlock(CanvasDrawingSession session, Rect region, TerminalScene scene, DesktopBlockFrame block, Rect content)
    {
        float cellHeight = Font.CellHeight;
        int first = Math.Max(0, (int)Math.Floor((region.Y - content.Y) / cellHeight));
        int last = Math.Min(block.Height - 1, (int)Math.Floor((region.Bottom - content.Y) / cellHeight));
        Rect clip = Intersect(content, region);
        if (first > last || clip.IsEmpty)
        {
            return;
        }

        using (session.CreateLayer(1, clip))
        {
            HashSet<int> matches = SearchCells(block, first, last);
            for (int row = first; row <= last; row++)
            {
                DrawBackgrounds(session, scene, block, content, row, matches);
            }

            DrawImages(session, block, content, region, behindText: true);
            for (int row = first; row <= last; row++)
            {
                DrawText(session, scene, block, content, row);
            }

            DrawImages(session, block, content, region, behindText: false);
            DrawCursor(session, scene, block, content, first, last);
        }
    }

    private void DrawBackgrounds(CanvasDrawingSession session, TerminalScene scene, DesktopBlockFrame block, Rect content,
        int row, HashSet<int> matches)
    {
        float cellWidth = Font.CellWidth;
        float top = (float)content.Y + (row * Font.CellHeight);
        int runStart = -1;
        Color runColor = default;
        for (int column = 0; column <= block.Width; column++)
        {
            Color? color = null;
            if (column < block.Width)
            {
                int index = (row * block.Width) + column;
                DesktopCell cell = block.Cells[index];
                bool reverse = (cell.Attributes & 32) != 0;
                color = block.Selection is { } selection && index >= selection.Start && index <= selection.End ? scene.Selection
                    : matches.Contains(index) ? s_match
                    : reverse ? (cell.Foreground is int foreground ? TerminalAppearance.FromRgb(foreground) : scene.Foreground)
                    : cell.Background is int background ? TerminalAppearance.FromRgb(background) : null;
            }

            if (runStart >= 0 && color != runColor)
            {
                // The surface was cleared to the default background; only other colors are painted.
                var rect = new Rect(content.X + (runStart * cellWidth), top, (column - runStart) * cellWidth, Font.CellHeight);
                session.FillRectangle(rect, runColor);
                runStart = -1;
            }

            if (color is { } value && runStart < 0)
            {
                runStart = column;
                runColor = value;
            }
        }
    }

    private void DrawText(CanvasDrawingSession session, TerminalScene scene, DesktopBlockFrame block, Rect content, int row)
    {
        float cellWidth = Font.CellWidth;
        float top = (float)content.Y + (row * Font.CellHeight);
        float baseline = top + Font.Baseline;
        int runStart = -1;
        CanvasFontFace? runFace = null;
        Color runColor = default;
        _glyphs.Clear();
        for (int column = 0; column < block.Width; column++)
        {
            DesktopCell cell = block.Cells[(row * block.Width) + column];
            int attributes = cell.Attributes;
            bool decorated = (attributes & 136) != 0;
            if (cell.Text.Length == 0 || (attributes & 64) != 0 || (cell.Text == " " && !decorated))
            {
                FlushRun(session, content, baseline, ref runStart, runFace, runColor);
                continue;
            }

            bool reverse = (attributes & 32) != 0;
            int? rgb = reverse ? cell.Background : cell.Foreground;
            Color color = rgb is int value ? TerminalAppearance.FromRgb(value) : reverse ? scene.Background : scene.Foreground;
            if ((attributes & 2) != 0)
            {
                color.A = 140;
            }

            bool wide = column + 1 < block.Width && block.Cells[(row * block.Width) + column + 1].Text.Length == 0;
            int glyph = 0;
            CanvasFontFace? face = null;
            if (!wide && Rune.DecodeFromUtf16(cell.Text, out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done
                && consumed == cell.Text.Length)
            {
                glyph = Font.Glyph(attributes, rune.Value, out face);
            }

            if (glyph != 0 && face is not null)
            {
                if (runStart < 0 || !ReferenceEquals(face, runFace) || color != runColor || runStart + _glyphs.Count != column)
                {
                    FlushRun(session, content, baseline, ref runStart, runFace, runColor);
                    runStart = column;
                    runFace = face;
                    runColor = color;
                }

                _glyphs.Add(new CanvasGlyph { Index = glyph, Advance = cellWidth });
            }
            else
            {
                FlushRun(session, content, baseline, ref runStart, runFace, runColor);
                var cellRect = new Rect(content.X + (column * cellWidth), top, cellWidth * (wide ? 2 : 1), Font.CellHeight);
                using (session.CreateLayer(1, cellRect))
                {
                    session.DrawText(cell.Text, new Vector2((float)cellRect.X, top + 1), color, Font.Format(attributes));
                }
            }

            if (decorated)
            {
                float left = (float)content.X + (column * cellWidth);
                float right = left + (cellWidth * (wide ? 2 : 1));
                if ((attributes & 8) != 0)
                {
                    session.DrawLine(new Vector2(left, baseline + 2), new Vector2(right, baseline + 2), color, 1);
                }

                if ((attributes & 128) != 0)
                {
                    float middle = baseline - (Font.CellHeight * 0.28f);
                    session.DrawLine(new Vector2(left, middle), new Vector2(right, middle), color, 1);
                }
            }
        }

        FlushRun(session, content, baseline, ref runStart, runFace, runColor);
    }

    private void FlushRun(CanvasDrawingSession session, Rect content, float baseline, ref int runStart, CanvasFontFace? face, Color color)
    {
        if (runStart < 0 || _glyphs.Count == 0)
        {
            runStart = -1;
            _glyphs.Clear();
            return;
        }

        if (face is not null)
        {
            var origin = new Vector2((float)content.X + (runStart * Font.CellWidth), baseline);
            session.DrawGlyphRun(origin, face, Font.Size, [.. _glyphs], false, 0, Brush(color));
        }

        runStart = -1;
        _glyphs.Clear();
    }

    private void DrawImages(CanvasDrawingSession session, DesktopBlockFrame block, Rect content, Rect region, bool behindText)
    {
        if (block.Images.Count == 0 || _device is null)
        {
            return;
        }

        var textures = new Dictionary<string, DesktopTexture>(StringComparer.Ordinal);
        foreach (DesktopTexture texture in block.Textures)
        {
            textures[TerminalImages.KeyOf(texture)] = texture;
        }

        float cellWidth = Font.CellWidth;
        float cellHeight = Font.CellHeight;
        IEnumerable<(DesktopImage Placement, DesktopTexture? Texture)> placements = block.Images
            .Where(image => (image.Layer < 0) == behindText)
            .Select(image => (image, textures.GetValueOrDefault(TerminalImages.KeyOf(image))));
        foreach ((DesktopImage placement, DesktopTexture? texture) in placements)
        {
            var clip = new Rect(content.X + (placement.ClipX * cellWidth), content.Y + (placement.ClipY * cellHeight),
                placement.ClipWidth * cellWidth, placement.ClipHeight * cellHeight);
            clip = Intersect(clip, region);
            if (texture is null || clip.IsEmpty || Images.Get(_device, texture) is not { } bitmap)
            {
                continue;
            }

            var destination = new Rect(content.X + (placement.X * cellWidth), content.Y + (placement.Y * cellHeight),
                placement.Width * cellWidth, placement.Height * cellHeight);
            using (session.CreateLayer(1, clip))
            {
                session.DrawImage(bitmap, destination, bitmap.Bounds, 1, CanvasImageInterpolation.NearestNeighbor);
            }
        }
    }

    private void DrawCursor(CanvasDrawingSession session, TerminalScene scene, DesktopBlockFrame block, Rect content, int first, int last)
    {
        if (!block.Active || !block.CursorVisible || block.Selection is not null || !scene.CursorLit
            || block.CursorY < first || block.CursorY > last)
        {
            return;
        }

        float cellWidth = Font.CellWidth;
        float cellHeight = Font.CellHeight;
        var cell = new Rect(content.X + (block.CursorX * cellWidth), content.Y + (block.CursorY * cellHeight), cellWidth, cellHeight);
        if (block.CursorShape is 3 or 4)
        {
            session.FillRectangle(new Rect(cell.X, cell.Bottom - 2, cellWidth, 2), scene.Cursor);
        }
        else if (block.CursorShape is 5 or 6)
        {
            session.FillRectangle(new Rect(cell.X, cell.Y, 2, cellHeight), scene.Cursor);
        }
        else
        {
            Color translucent = scene.Cursor;
            translucent.A = 153;
            session.FillRectangle(cell, translucent);
        }
    }

    private void DrawChrome(CanvasDrawingSession session, TerminalScene scene, DesktopBlockFrame block, Rect content, Rect pane)
    {
        var border = Color.FromArgb(block.Active ? (byte)41 : (byte)18, 255, 255, 255);
        session.DrawRoundedRectangle(pane, 6, 6, border, 1);
        _chrome ??= new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text",
            FontSize = 11,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            TrimmingSign = CanvasTrimmingSign.Ellipsis,
            TrimmingDelimiter = "/",
            TrimmingDelimiterCount = 2,
            VerticalAlignment = CanvasVerticalAlignment.Center
        };
        Color secondary = scene.Foreground;
        secondary.A = 150;
        bool inspecting = block.ViewVersion != 0;
        var title = new Rect(content.X + 5, pane.Y + 1, Math.Max(0, content.Width - (inspecting ? 145 : 10)), Font.CellHeight - 2);
        if (title.Width > 0)
        {
            session.DrawText(block.Title, title, secondary, _chrome);
        }

        if (inspecting)
        {
            string label = block.Selection is not null ? "Selection · Resume ↓" : "History · Resume ↓";
            session.DrawText(label, new Rect(content.Right - 135, pane.Y + 1, 130, Font.CellHeight - 2), secondary, _chrome);
        }
    }

    private CanvasSolidColorBrush Brush(Color color)
    {
        if (_brushes.TryGetValue(color, out CanvasSolidColorBrush? brush))
        {
            return brush;
        }

        if (_brushes.Count > 512)
        {
            foreach (CanvasSolidColorBrush stale in _brushes.Values)
            {
                stale.Dispose();
            }

            _brushes.Clear();
        }

        brush = new CanvasSolidColorBrush(_device, color);
        _brushes[color] = brush;
        return brush;
    }

    private static bool Intersects(Rect first, Rect second)
    {
        return !Intersect(first, second).IsEmpty;
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        first.Intersect(second);
        return first.Width <= 0 || first.Height <= 0 ? Rect.Empty : first;
    }
}
