using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Weft.Desktop.Windows;

/// <summary>
/// Resolves the terminal font, its styled faces, and the cell grid they share.
/// </summary>
/// <remarks>
/// The bundled Cascadia Mono NF is loaded from the app directory for this process only. Other fonts come
/// from the system font set. Cells use explicit advances, so glyphs keep the grid whatever their widths.
/// </remarks>
internal sealed partial class TerminalFont : IDisposable
{
    /// <summary>
    /// The bundled family, which carries Powerline and Nerd Font symbols.
    /// </summary>
    internal const string BundledFamily = "Cascadia Mono NF";

    /// <summary>
    /// The default font size in device-independent pixels.
    /// </summary>
    internal const float DefaultSize = 14;

    private const int SymbolSlot = 4;
    private readonly Dictionary<(int Style, int CodePoint), (int Slot, int Glyph)> _glyphs = [];
    private readonly CanvasFontFace?[] _faces = new CanvasFontFace?[4];
    private readonly CanvasTextFormat?[] _formats = new CanvasTextFormat?[4];
    private readonly List<IDisposable> _owned = [];
    private CanvasFontFace? _symbols;

    private TerminalFont(string family, float size, string formatFamily)
    {
        Family = family;
        Size = size;
        FormatFamily = formatFamily;
    }

    /// <summary>
    /// Gets the family name shown to the user.
    /// </summary>
    internal string Family { get; }

    /// <summary>
    /// Gets the size in device-independent pixels.
    /// </summary>
    internal float Size { get; }

    /// <summary>
    /// Gets the family name, or font file reference, used for shaped fallback text.
    /// </summary>
    internal string FormatFamily { get; }

    /// <summary>
    /// Gets the cell width in device-independent pixels.
    /// </summary>
    internal float CellWidth { get; private set; }

    /// <summary>
    /// Gets the cell height in device-independent pixels.
    /// </summary>
    internal float CellHeight { get; private set; }

    /// <summary>
    /// Gets the distance from a cell's top edge to the text baseline.
    /// </summary>
    internal float Baseline { get; private set; }

    /// <summary>
    /// Creates the preferred font, falling back to installed monospaced families.
    /// </summary>
    /// <param name="device">The drawing device used to measure the font.</param>
    /// <param name="family">The preferred family, or null for the bundled font.</param>
    /// <param name="size">The preferred size, or null for the default.</param>
    /// <returns>The font.</returns>
    internal static TerminalFont Create(CanvasDevice device, string? family, double? size)
    {
        ArgumentNullException.ThrowIfNull(device);
        float points = (float)Math.Clamp(size ?? DefaultSize, 8, 40);
        string directory = Path.Join(AppContext.BaseDirectory, "Fonts");
        string regular = Path.Join(directory, "CascadiaMonoNF.ttf");
        bool bundled = (family is null || family == BundledFamily) && File.Exists(regular);
        string resolved = bundled
            ? BundledFamily
            : new[] { family, "Cascadia Mono", "Consolas" }
                .OfType<string>()
                .FirstOrDefault(name => name != BundledFamily && HasSystemFamily(name)) ?? "Consolas";
        var font = new TerminalFont(resolved, points,
            bundled ? new Uri(regular).AbsoluteUri + "#" + BundledFamily : resolved);
        try
        {
            if (bundled)
            {
                font.LoadBundled(regular, Path.Join(directory, "CascadiaMonoNFItalic.ttf"));
            }
            else
            {
                font.LoadSystem();
                if (File.Exists(regular))
                {
                    // Like the Mac cascade list, the bundled font supplies Powerline and Nerd Font symbols.
                    font.LoadSymbols(regular);
                }
            }

            font.Measure(device);
            return font;
        }
        catch
        {
            font.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the installed monospaced families offered in settings, with the bundled font first.
    /// </summary>
    /// <returns>The family names.</returns>
    internal static IReadOnlyList<string> MonospacedFamilies()
    {
        using var system = CanvasFontSet.GetSystemFontSet();
        var families = new SortedSet<string>(system.Fonts
            .Where(face => face.IsMonospaced)
            .Select(face => face.FamilyNames.GetValueOrDefault("en-us"))
            .OfType<string>(), StringComparer.OrdinalIgnoreCase);

        return new[] { BundledFamily }.Concat(families.Where(name => name != BundledFamily)).ToArray();
    }

    /// <summary>
    /// Gets the face for bold and italic attributes, or null when no face could be loaded.
    /// </summary>
    /// <param name="attributes">The terminal cell attributes.</param>
    /// <returns>The face.</returns>
    internal CanvasFontFace? Face(int attributes)
    {
        int index = StyleIndex(attributes);
        return _faces[index] ?? _faces[index & 2] ?? _faces[0];
    }

    /// <summary>
    /// Gets a text format for shaped fallback text with the style's weight and slant.
    /// </summary>
    /// <param name="attributes">The terminal cell attributes.</param>
    /// <returns>The format.</returns>
    internal CanvasTextFormat Format(int attributes)
    {
        int index = StyleIndex(attributes);
        if (_formats[index] is { } existing)
        {
            return existing;
        }

        var format = new CanvasTextFormat
        {
            FontFamily = FormatFamily,
            FontSize = Size,
            FontWeight = (index & 1) != 0 ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = (index & 2) != 0 ? FontStyle.Italic : FontStyle.Normal,
            WordWrapping = CanvasWordWrapping.NoWrap,
            // Text drawn at a point has an empty layout box, which clipping would hide entirely. The renderer
            // clips each cell with a layer instead.
            Options = CanvasDrawTextOptions.EnableColorFont
        };
        _formats[index] = format;
        return format;
    }

    /// <summary>
    /// Gets a glyph for a character from the style's face, or from the bundled symbol face when the style lacks it.
    /// </summary>
    /// <param name="attributes">The terminal cell attributes.</param>
    /// <param name="codePoint">The Unicode scalar value.</param>
    /// <param name="face">Receives the face that holds the glyph.</param>
    /// <returns>The glyph index, or zero when neither face has the character.</returns>
    internal int Glyph(int attributes, int codePoint, out CanvasFontFace? face)
    {
        int style = StyleIndex(attributes);
        if (!_glyphs.TryGetValue((style, codePoint), out (int Slot, int Glyph) entry))
        {
            int glyph = Face(attributes)?.GetGlyphIndices([(uint)codePoint])[0] ?? 0;
            int symbol = glyph == 0 && _symbols is not null ? _symbols.GetGlyphIndices([(uint)codePoint])[0] : 0;
            entry = symbol != 0 ? (SymbolSlot, symbol) : (style, glyph);
            _glyphs[(style, codePoint)] = entry;
        }

        face = entry.Slot == SymbolSlot ? _symbols : Face(attributes);
        return entry.Glyph;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (CanvasTextFormat? format in _formats)
        {
            format?.Dispose();
        }

        foreach (IDisposable owned in _owned)
        {
            owned.Dispose();
        }

        _glyphs.Clear();
    }

    private static int StyleIndex(int attributes)
    {
        return ((attributes & 1) != 0 ? 1 : 0) | ((attributes & 4) != 0 ? 2 : 0);
    }

    private static bool HasSystemFamily(string family)
    {
        using var system = CanvasFontSet.GetSystemFontSet();
        using CanvasFontSet matches = system.GetMatchingFonts(family, FontWeights.Normal, FontStretch.Normal,
            FontStyle.Normal);
        return matches.Fonts.Any(face =>
            face.FamilyNames.Values.Any(name => string.Equals(name, family, StringComparison.OrdinalIgnoreCase)));
    }

    private void LoadBundled(string regular, string italic)
    {
        var upright = new CanvasFontSet(new Uri(regular));
        _owned.Add(upright);
        _faces[0] = Match(upright, FontWeights.Normal, FontStyle.Normal);
        _faces[1] = Match(upright, FontWeights.Bold, FontStyle.Normal);
        if (File.Exists(italic))
        {
            var slanted = new CanvasFontSet(new Uri(italic));
            _owned.Add(slanted);
            _faces[2] = Match(slanted, FontWeights.Normal, FontStyle.Italic);
            _faces[3] = Match(slanted, FontWeights.Bold, FontStyle.Italic);
        }
    }

    private void LoadSymbols(string path)
    {
        var symbols = new CanvasFontSet(new Uri(path));
        _owned.Add(symbols);
        CanvasFontSet matches = symbols.GetMatchingFonts(BundledFamily, FontWeights.Normal, FontStretch.Normal,
            FontStyle.Normal);
        _owned.Add(matches);
        _symbols = matches.Fonts.Count == 0 ? null : matches.Fonts[0];
    }

    private void LoadSystem()
    {
        var system = CanvasFontSet.GetSystemFontSet();
        _owned.Add(system);
        _faces[0] = Match(system, FontWeights.Normal, FontStyle.Normal);
        _faces[1] = Match(system, FontWeights.Bold, FontStyle.Normal);
        _faces[2] = Match(system, FontWeights.Normal, FontStyle.Italic);
        _faces[3] = Match(system, FontWeights.Bold, FontStyle.Italic);
    }

    private CanvasFontFace? Match(CanvasFontSet set, FontWeight weight, FontStyle style)
    {
        CanvasFontSet matches = set.GetMatchingFonts(Family, weight, FontStretch.Normal, style);
        _owned.Add(matches);
        return matches.Fonts.Count == 0 ? null : matches.Fonts[0];
    }

    private void Measure(CanvasDevice device)
    {
        using var layout = new CanvasTextLayout(device, "M", Format(0), 1000, 1000);
        CanvasLineMetrics line = layout.LineMetrics[0];
        CellWidth = MathF.Ceiling((float)layout.LayoutBounds.Width);
        CellHeight = MathF.Ceiling(line.Height) + 2;
        Baseline = line.Baseline + 1;
    }
}
