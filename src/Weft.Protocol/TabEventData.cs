namespace Weft.Protocol;

/// <summary>
/// Payload of tab events.
/// </summary>
public sealed class TabEventData
{
    /// <summary>
    /// Gets the tab.
    /// </summary>
    public required TabInfo Tab { get; init; }
}
