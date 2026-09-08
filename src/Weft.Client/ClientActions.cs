namespace Weft.Client;

/// <summary>
/// The action ids the attach client can bind to chords.
/// </summary>
internal static class ClientActions
{
    /// <summary>Detach from the session.</summary>
    internal const string Detach = "detach";

    /// <summary>Create a tab.</summary>
    internal const string TabNew = "tab.new";

    /// <summary>Select the next tab.</summary>
    internal const string TabNext = "tab.next";

    /// <summary>Select the previous tab.</summary>
    internal const string TabPrevious = "tab.previous";

    /// <summary>Close the current tab.</summary>
    internal const string TabClose = "tab.close";

    /// <summary>Rename the current tab.</summary>
    internal const string TabRename = "tab.rename";

    /// <summary>Split the focused block to the right.</summary>
    internal const string SplitRight = "split.right";

    /// <summary>Split the focused block below.</summary>
    internal const string SplitDown = "split.down";

    /// <summary>Close the focused block.</summary>
    internal const string BlockClose = "block.close";

    /// <summary>Toggle zoom on the focused block.</summary>
    internal const string BlockZoom = "block.zoom";

    /// <summary>Float or re-tile the focused block.</summary>
    internal const string BlockFloat = "block.float";

    /// <summary>Rename the focused block.</summary>
    internal const string BlockRename = "block.rename";

    /// <summary>Enter copy mode on the focused block.</summary>
    internal const string CopyMode = "copy-mode";

    /// <summary>Paste the server paste buffer into the focused block.</summary>
    internal const string Paste = "paste";

    /// <summary>Focus the block to the left.</summary>
    internal const string FocusLeft = "focus.left";

    /// <summary>Focus the block to the right.</summary>
    internal const string FocusRight = "focus.right";

    /// <summary>Focus the block above.</summary>
    internal const string FocusUp = "focus.up";

    /// <summary>Focus the block below.</summary>
    internal const string FocusDown = "focus.down";

    /// <summary>Grow the focused block leftward.</summary>
    internal const string ResizeLeft = "resize.left";

    /// <summary>Grow the focused block rightward.</summary>
    internal const string ResizeRight = "resize.right";

    /// <summary>Grow the focused block upward.</summary>
    internal const string ResizeUp = "resize.up";

    /// <summary>Grow the focused block downward.</summary>
    internal const string ResizeDown = "resize.down";

    /// <summary>Cycle to the next layout preset.</summary>
    internal const string LayoutNext = "layout.next";

    /// <summary>Open the session picker.</summary>
    internal const string SessionPick = "session.pick";

    /// <summary>Rename the session.</summary>
    internal const string SessionRename = "session.rename";

    /// <summary>Open the tab and block picker.</summary>
    internal const string TabPick = "tab.pick";

    /// <summary>Toggle locked mode, which passes every key to the block.</summary>
    internal const string Lock = "lock";

    /// <summary>Open the command palette.</summary>
    internal const string Palette = "palette";

    /// <summary>Send the leader chord's first stroke to the focused block.</summary>
    internal const string SendLeader = "send-leader";

    /// <summary>
    /// Gets the default chord for every action, in palette order.
    /// </summary>
    internal static IReadOnlyList<(string Action, string Chord, string Description)> Defaults { get; } =
    [
        (Palette, "leader ?", "Command palette and help"),
        (Detach, "leader d", "Detach from the session"),
        (TabNew, "leader c", "New tab"),
        (TabNext, "leader n", "Next tab"),
        (TabPrevious, "leader p", "Previous tab"),
        (TabClose, "leader shift+x", "Close tab"),
        (TabRename, "leader .", "Rename tab"),
        (SplitRight, "leader v", "Split right"),
        (SplitDown, "leader -", "Split below"),
        (BlockClose, "leader x", "Close block"),
        (BlockZoom, "leader z", "Zoom block"),
        (BlockFloat, "leader f", "Float or tile block"),
        (BlockRename, "leader ,", "Rename block"),
        (CopyMode, "leader pageup", "Copy mode and scrollback"),
        (Paste, "leader insert", "Paste"),
        (FocusLeft, "leader h", "Focus left"),
        (FocusDown, "leader j", "Focus down"),
        (FocusUp, "leader k", "Focus up"),
        (FocusRight, "leader l", "Focus right"),
        (ResizeLeft, "leader shift+h", "Resize left"),
        (ResizeDown, "leader shift+j", "Resize down"),
        (ResizeUp, "leader shift+k", "Resize up"),
        (ResizeRight, "leader shift+l", "Resize right"),
        (LayoutNext, "leader space", "Next layout preset"),
        (SessionPick, "leader s", "Switch session"),
        (SessionRename, "leader shift+s", "Rename session"),
        (TabPick, "leader w", "Pick tab or block"),
        (Lock, "leader g", "Lock: pass every key through"),
        (SendLeader, "leader ctrl+b", "Send the leader key")
    ];

    /// <summary>
    /// Gets extra default chords that alias actions.
    /// </summary>
    internal static IReadOnlyList<(string Action, string Chord)> Aliases { get; } =
    [
        (FocusLeft, "leader left"),
        (FocusDown, "leader down"),
        (FocusUp, "leader up"),
        (FocusRight, "leader right"),
        (TabNext, "leader tab")
    ];
}
