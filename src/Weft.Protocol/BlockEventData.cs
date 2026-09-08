namespace Weft.Protocol;

/// <summary>
/// Payload of block events.
/// </summary>
public sealed class BlockEventData
{
    /// <summary>
    /// Gets the block.
    /// </summary>
    public required BlockInfo Block { get; init; }
}
