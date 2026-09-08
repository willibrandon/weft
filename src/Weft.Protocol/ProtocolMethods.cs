namespace Weft.Protocol;

/// <summary>
/// The method names of the control protocol.
/// </summary>
public static class ProtocolMethods
{
    /// <summary>Returns server identity and counts.</summary>
    public const string ServerInfo = "server.info";

    /// <summary>Shuts the server down gracefully.</summary>
    public const string ServerShutdown = "server.shutdown";

    /// <summary>Lists sessions.</summary>
    public const string SessionList = "session.list";

    /// <summary>Creates a session.</summary>
    public const string SessionCreate = "session.create";

    /// <summary>Gets one session.</summary>
    public const string SessionGet = "session.get";

    /// <summary>Renames a session.</summary>
    public const string SessionRename = "session.rename";

    /// <summary>Closes a session and its blocks.</summary>
    public const string SessionClose = "session.close";

    /// <summary>Registers a client viewport on a session.</summary>
    public const string SessionAttach = "session.attach";

    /// <summary>Releases a client viewport.</summary>
    public const string SessionDetach = "session.detach";

    /// <summary>Reports a client's viewport size.</summary>
    public const string SessionSetSize = "session.setSize";

    /// <summary>Lists tabs in a session.</summary>
    public const string TabList = "tab.list";

    /// <summary>Creates a tab.</summary>
    public const string TabCreate = "tab.create";

    /// <summary>Selects a tab.</summary>
    public const string TabSelect = "tab.select";

    /// <summary>Renames a tab.</summary>
    public const string TabRename = "tab.rename";

    /// <summary>Closes a tab and its blocks.</summary>
    public const string TabClose = "tab.close";

    /// <summary>Lists blocks.</summary>
    public const string BlockList = "block.list";

    /// <summary>Gets one block.</summary>
    public const string BlockGet = "block.get";

    /// <summary>Splits a block to create another.</summary>
    public const string BlockSplit = "block.split";

    /// <summary>Closes a block.</summary>
    public const string BlockClose = "block.close";

    /// <summary>Sends a signal to a block's process.</summary>
    public const string BlockKill = "block.kill";

    /// <summary>Sets a block's title.</summary>
    public const string BlockRename = "block.rename";

    /// <summary>Focuses a block, optionally by direction.</summary>
    public const string BlockFocus = "block.focus";

    /// <summary>Toggles or sets zoom on a block.</summary>
    public const string BlockZoom = "block.zoom";

    /// <summary>Swaps two blocks' positions.</summary>
    public const string BlockSwap = "block.swap";

    /// <summary>Sends named keys and text to a block.</summary>
    public const string BlockSendKeys = "block.sendKeys";

    /// <summary>Types literal text into a block.</summary>
    public const string BlockType = "block.type";

    /// <summary>Pastes text into a block with bracketed paste when supported.</summary>
    public const string BlockPaste = "block.paste";

    /// <summary>Captures a block's screen and optional history.</summary>
    public const string BlockCapture = "block.capture";

    /// <summary>Waits for a pattern, exit, or change on a block.</summary>
    public const string BlockWait = "block.wait";

    /// <summary>Runs a command in a new block and awaits its exit.</summary>
    public const string BlockRun = "block.run";

    /// <summary>Gets a tab's layout and geometry.</summary>
    public const string LayoutGet = "layout.get";

    /// <summary>Applies a serialized layout to a tab.</summary>
    public const string LayoutApply = "layout.apply";

    /// <summary>Rebuilds a tab's layout into a preset.</summary>
    public const string LayoutPreset = "layout.preset";

    /// <summary>Resizes a block by moving one edge.</summary>
    public const string LayoutResize = "layout.resize";

    /// <summary>Streams events from a sequence number.</summary>
    public const string EventsSubscribe = "events.subscribe";

    /// <summary>Gets the paste buffer.</summary>
    public const string PasteGet = "paste.get";

    /// <summary>Sets the paste buffer.</summary>
    public const string PasteSet = "paste.set";

    /// <summary>Waits on a named channel until it is signalled.</summary>
    public const string WaitFor = "wait.for";

    /// <summary>Signals a named channel.</summary>
    public const string WaitSignal = "wait.signal";
}
