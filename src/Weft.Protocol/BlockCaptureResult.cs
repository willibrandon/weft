namespace Weft.Protocol;

/// <summary>
/// Result of block.capture.
/// </summary>
public sealed class BlockCaptureResult
{
    /// <summary>
    /// Gets the block id.
    /// </summary>
    public required string Block { get; init; }

    /// <summary>
    /// Gets the output revision the capture reflects.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the screen width in columns.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the screen height in rows.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the cursor column.
    /// </summary>
    public required int CursorX { get; init; }

    /// <summary>
    /// Gets the cursor row.
    /// </summary>
    public required int CursorY { get; init; }

    /// <summary>
    /// Gets the number of history lines included before the screen lines.
    /// </summary>
    public required int HistoryLines { get; init; }

    /// <summary>
    /// Gets the lines, history first then screen.
    /// </summary>
    public required IReadOnlyList<string> Lines { get; init; }
}
