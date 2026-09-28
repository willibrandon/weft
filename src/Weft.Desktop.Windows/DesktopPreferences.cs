namespace Weft.Desktop.Windows;

/// <summary>
/// Holds the app's appearance and shortcut choices; shell configuration stays in the user's Weft config.
/// </summary>
internal sealed record DesktopPreferences
{
    /// <summary>
    /// Gets the terminal font family, or null for the bundled font.
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>
    /// Gets the terminal font size in device-independent pixels, or null for the default.
    /// </summary>
    public double? FontSize { get; init; }

    /// <summary>
    /// Gets the six-digit RGB terminal background, such as <c>#13161a</c>.
    /// </summary>
    public string? Background { get; init; }

    /// <summary>
    /// Gets the six-digit RGB default text color.
    /// </summary>
    public string? Foreground { get; init; }

    /// <summary>
    /// Gets the six-digit RGB cursor color.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// Gets shortcut overrides by action identifier; an empty value removes the default shortcut.
    /// </summary>
    public IReadOnlyDictionary<string, string> Shortcuts { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
