namespace Weft.Protocol;

/// <summary>
/// Payload of layout.changed.
/// </summary>
public sealed class LayoutEventData
{
    /// <summary>
    /// Gets the layout.
    /// </summary>
    public required LayoutInfo Layout { get; init; }
}
