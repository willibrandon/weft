namespace Weft.Protocol;

/// <summary>
/// Parameters for block.sendKeys.
/// </summary>
public sealed class BlockSendKeysParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the keys: names such as <c>Enter</c>, <c>C-c</c>, or <c>Up</c> are encoded, anything else is typed.
    /// </summary>
    public required IReadOnlyList<string> Keys { get; init; }

    /// <summary>
    /// Gets whether every item is typed literally instead of interpreted as a key name.
    /// </summary>
    public bool Literal { get; init; }
}
