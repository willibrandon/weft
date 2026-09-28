using System.Globalization;
using Windows.UI;

namespace Weft.Desktop.Windows;

/// <summary>
/// Resolves app colors from preferences, independently of shell prompt configuration.
/// </summary>
internal static class TerminalAppearance
{
    /// <summary>
    /// The default terminal background.
    /// </summary>
    internal const int DefaultBackground = 0x13161a;

    /// <summary>
    /// The default terminal text color.
    /// </summary>
    internal const int DefaultForeground = 0xe6e6e6;

    /// <summary>
    /// The default cursor color.
    /// </summary>
    internal const int DefaultCursor = 0x80a7c2;

    /// <summary>
    /// Gets the terminal background.
    /// </summary>
    internal static Color Background => Resolve(PreferencesStore.Current.Background, DefaultBackground);

    /// <summary>
    /// Gets the default text color.
    /// </summary>
    internal static Color Foreground => Resolve(PreferencesStore.Current.Foreground, DefaultForeground);

    /// <summary>
    /// Gets the cursor color.
    /// </summary>
    internal static Color Cursor => Resolve(PreferencesStore.Current.Cursor, DefaultCursor);

    /// <summary>
    /// Converts a packed RGB value to an opaque color.
    /// </summary>
    /// <param name="rgb">The packed 0xRRGGBB value.</param>
    /// <returns>The color.</returns>
    internal static Color FromRgb(int rgb)
    {
        return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    /// <summary>
    /// Formats a color as the six-digit form stored in preferences.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The <c>#rrggbb</c> text.</returns>
    internal static string Format(Color color)
    {
        return string.Create(CultureInfo.InvariantCulture, $"#{color.R:x2}{color.G:x2}{color.B:x2}");
    }

    private static Color Resolve(string? text, int fallback)
    {
        string trimmed = text?.Trim().TrimStart('#') ?? string.Empty;
        int rgb = fallback;
        if (trimmed.Length == 6
            && int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsed))
        {
            rgb = parsed;
        }

        return FromRgb(rgb);
    }
}
