namespace Weft.Protocol;

/// <summary>
/// Parameters for tab.sync.
/// </summary>
public sealed class TabSyncParams
{
    /// <summary>
    /// Gets the target tab.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets whether input is synchronized; null toggles.
    /// </summary>
    public bool? Enabled { get; init; }
}
