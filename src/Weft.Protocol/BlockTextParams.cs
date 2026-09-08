namespace Weft.Protocol;

/// <summary>
/// Parameters for block.type and block.paste.
/// </summary>
public sealed class BlockTextParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public required string Text { get; init; }
}
