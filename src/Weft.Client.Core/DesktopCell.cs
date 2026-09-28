using System.Text.Json.Serialization;

namespace Weft.Client;

/// <summary>
/// A grapheme and its resolved style at one terminal cell.
/// </summary>
/// <param name="Text">The grapheme, or an empty string for a wide character continuation.</param>
/// <param name="Foreground">The RGB foreground, or null for the application default.</param>
/// <param name="Background">The RGB background, or null for the application default.</param>
/// <param name="Attributes">The terminal cell attribute flags.</param>
/// <param name="Link">The terminal hyperlink, if present.</param>
[JsonConverter(typeof(DesktopCellJsonConverter))]
public readonly record struct DesktopCell(string Text, int? Foreground, int? Background, int Attributes, string? Link = null);
