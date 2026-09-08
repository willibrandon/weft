namespace Weft.Protocol;

/// <summary>
/// A tab as reported by the server.
/// </summary>
public sealed class TabInfo
{
    /// <summary>
    /// Gets the tab id in text form.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the one-based index within the session.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Gets the tab name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the owning session id.
    /// </summary>
    public required string Session { get; init; }

    /// <summary>
    /// Gets the number of blocks in the tab.
    /// </summary>
    public required int Blocks { get; init; }

    /// <summary>
    /// Gets the active block id, if any.
    /// </summary>
    public string? ActiveBlock { get; init; }

    /// <summary>
    /// Gets the zoomed block id, if any.
    /// </summary>
    public string? Zoomed { get; init; }

    /// <summary>
    /// Gets whether the tab is the session's active tab.
    /// </summary>
    public required bool Active { get; init; }

    /// <summary>
    /// Gets whether typed input is synchronized to every block in the tab.
    /// </summary>
    public required bool Synchronized { get; init; }
}
