using Weft.Protocol;

namespace Weft.Client;

/// <summary>
/// An immutable desktop update; taking one never waits for network activity.
/// </summary>
/// <param name="Connected">Whether commands can be sent.</param>
/// <param name="Title">The session name.</param>
/// <param name="ActiveTab">The selected tab id.</param>
/// <param name="Error">The latest failure, if any.</param>
/// <param name="Tabs">The available tabs.</param>
/// <param name="Blocks">The visible block snapshots.</param>
/// <param name="Sessions">The available sessions.</param>
/// <param name="ActiveSession">The attached session id.</param>
public sealed record DesktopFrame(bool Connected, string Title, string? ActiveTab, string? Error,
    IReadOnlyList<TabInfo> Tabs, IReadOnlyList<DesktopBlockFrame> Blocks,
    IReadOnlyList<SessionInfo> Sessions, string? ActiveSession)
{
    /// <summary>
    /// Gets the shared action catalog for native menus and command search.
    /// </summary>
    public IReadOnlyList<DesktopAction> Commands { get; init; } = DesktopActions.All;
}
