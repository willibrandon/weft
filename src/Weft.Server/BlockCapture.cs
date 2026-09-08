namespace Weft.Server;

/// <summary>
/// A capture of a block's screen and optional history.
/// </summary>
/// <param name="Revision">The output revision the capture reflects.</param>
/// <param name="Width">The screen width.</param>
/// <param name="Height">The screen height.</param>
/// <param name="CursorX">The cursor column.</param>
/// <param name="CursorY">The cursor row.</param>
/// <param name="HistoryLines">How many history lines precede the screen lines.</param>
/// <param name="Lines">The lines, history first.</param>
/// <param name="ApplicationCursorKeys">Whether application cursor keys were enabled.</param>
/// <param name="BracketedPaste">Whether bracketed paste was enabled.</param>
internal sealed record BlockCapture(
    long Revision,
    int Width,
    int Height,
    int CursorX,
    int CursorY,
    int HistoryLines,
    IReadOnlyList<string> Lines,
    bool ApplicationCursorKeys,
    bool BracketedPaste);
