namespace Weft.Protocol;

/// <summary>
/// Parameters for block.close.
/// </summary>
public sealed class BlockCloseParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }
}
