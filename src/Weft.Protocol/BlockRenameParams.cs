namespace Weft.Protocol;

/// <summary>
/// Parameters for block.rename.
/// </summary>
public sealed class BlockRenameParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the title to pin; null or empty unpins and follows the terminal title again.
    /// </summary>
    public string? Title { get; init; }
}
