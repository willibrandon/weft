namespace Weft.Protocol;

/// <summary>
/// Parameters for paste.set and result of paste.get.
/// </summary>
public sealed class PasteBuffer
{
    /// <summary>
    /// Gets the buffer text.
    /// </summary>
    public required string Text { get; init; }
}
