namespace Weft.Client;

/// <summary>
/// A retained terminal selection whose cell bounds may extend beyond the visible viewport.
/// </summary>
/// <param name="Start">The first selected cell relative to the viewport origin.</param>
/// <param name="End">The last selected cell relative to the viewport origin.</param>
/// <param name="Text">The selected text with hard line breaks and wide graphemes preserved.</param>
public sealed record DesktopSelection(int Start, int End, string Text);
