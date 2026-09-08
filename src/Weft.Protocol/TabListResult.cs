namespace Weft.Protocol;

/// <summary>
/// Result of tab.list.
/// </summary>
public sealed class TabListResult
{
    /// <summary>
    /// Gets the tabs in index order.
    /// </summary>
    public required IReadOnlyList<TabInfo> Tabs { get; init; }
}
