namespace Weft.Client;

/// <summary>
/// Supplies the desktop command names shared by menus and command search.
/// </summary>
public static class DesktopActions
{
    /// <summary>
    /// Gets the command catalog; each platform supplies its own shortcut mapping.
    /// </summary>
    public static IReadOnlyList<DesktopAction> All { get; } = Array.AsReadOnly<DesktopAction>(
    [
        new("newSession", "New Session…", "Session", "window"),
        new("renameSession", "Rename Session…", "Session", "session"),
        new("closeSession", "Close Session…", "Session", "session", true),
        new("newTab", "New Tab", "Tab", "session"),
        new("nextTab", "Next Tab", "Tab", "tab"),
        new("previousTab", "Previous Tab", "Tab", "tab"),
        new("renameTab", "Rename Tab…", "Tab", "tab"),
        new("closeTab", "Close Tab…", "Tab", "tab", true),
        new("splitRight", "Split Right", "Pane", "block"),
        new("splitBelow", "Split Below", "Pane", "block"),
        new("zoom", "Zoom Pane", "Pane", "block"),
        new("renameBlock", "Rename Pane…", "Pane", "block"),
        new("closeBlock", "Close Pane…", "Pane", "block", true),
        new("float", "Float Pane", "Pane", "block"),
        new("tile", "Tile Pane", "Pane", "block"),
        new("layout", "Arrange Panes", "Pane", "tab"),
        new("sync", "Broadcast Input…", "Pane", "tab"),
        new("find", "Find in Terminal…", "Edit", "block"),
        new("live", "Resume Live Output", "View", "block"),
        new("font", "Choose Font…", "View", "window"),
        new("commands", "Commands…", "View", "window"),
        new("reconnect", "Reconnect", "View", "window")
    ]);
}
