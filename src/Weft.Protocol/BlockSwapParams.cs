namespace Weft.Protocol;

/// <summary>
/// Parameters for block.swap.
/// </summary>
public sealed class BlockSwapParams
{
    /// <summary>
    /// Gets the first block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the block to exchange places with.
    /// </summary>
    public required string With { get; init; }
}
