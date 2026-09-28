namespace Weft.Client;

/// <summary>
/// An immutable visible block snapshot in session cell coordinates.
/// </summary>
/// <param name="Id">The block id.</param>
/// <param name="Title">The untrusted terminal title displayed as plain text.</param>
/// <param name="Active">Whether this is the active block.</param>
/// <param name="X">The content column, excluding the server frame.</param>
/// <param name="Y">The content row, excluding the server frame.</param>
/// <param name="Width">The snapshot columns.</param>
/// <param name="Height">The snapshot rows.</param>
/// <param name="CursorX">The cursor column.</param>
/// <param name="CursorY">The cursor row.</param>
/// <param name="CursorVisible">Whether to display the cursor.</param>
/// <param name="CursorShape">The DECSCUSR cursor shape.</param>
/// <param name="Cells">The row-major terminal cells.</param>
/// <param name="HistoryLines">The retained history available to this view.</param>
/// <param name="ScrollOffset">The distance above the live screen.</param>
/// <param name="AlternateScreen">Whether a full-screen terminal application is active.</param>
/// <param name="MouseTracking">Whether the application accepts mouse input.</param>
/// <param name="ViewVersion">The identity of a frozen history view, or zero for live output.</param>
/// <param name="SearchQuery">The current literal search text.</param>
/// <param name="SearchMatches">The number of matching retained rows.</param>
public sealed record DesktopBlockFrame(string Id, string Title, bool Active, int X, int Y, int Width, int Height,
    int CursorX, int CursorY, bool CursorVisible, int CursorShape, IReadOnlyList<DesktopCell> Cells,
    int HistoryLines = 0, int ScrollOffset = 0, bool AlternateScreen = false, bool MouseTracking = false, long ViewVersion = 0,
    string SearchQuery = "", int SearchMatches = 0)
{
    /// <summary>
    /// Gets bounded raster placements for this viewport.
    /// </summary>
    public IReadOnlyList<DesktopImage> Images { get; init; } = [];

    /// <summary>
    /// Gets the unique textures referenced by the viewport's image placements.
    /// </summary>
    public IReadOnlyList<DesktopTexture> Textures { get; init; } = [];

    /// <summary>
    /// Gets the selection retained with the history snapshot, including text outside the viewport.
    /// </summary>
    public DesktopSelection? Selection { get; init; }
}
