using Weft.Protocol;

namespace Weft.Client;

/// <summary>
/// The client's copy of a session's structure, kept current from the server's event stream.
/// </summary>
internal sealed class SessionMirror
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, BlockInfo> _blocks = new(StringComparer.Ordinal);
    private readonly List<TabInfo> _tabs = [];

    /// <summary>
    /// Initializes the mirror from an attach snapshot.
    /// </summary>
    /// <param name="snapshot">The attach result.</param>
    internal SessionMirror(SessionAttachResult snapshot)
    {
        Client = snapshot.Client;
        Session = snapshot.Session;
        Layout = snapshot.Layout;
        _tabs.AddRange(snapshot.Tabs);
        foreach (BlockInfo block in snapshot.Blocks)
        {
            _blocks[block.Id] = block;
        }
    }

    /// <summary>
    /// Gets the attached client record.
    /// </summary>
    internal ClientInfo Client { get; }

    /// <summary>
    /// Gets the session.
    /// </summary>
    internal SessionInfo Session { get; private set; }

    /// <summary>
    /// Gets the active tab's geometry.
    /// </summary>
    internal LayoutInfo Layout { get; private set; }

    /// <summary>
    /// Gets whether the session or server has gone away.
    /// </summary>
    internal bool Closed { get; private set; }

    /// <summary>
    /// Gets the tabs in index order.
    /// </summary>
    internal IReadOnlyList<TabInfo> Tabs
    {
        get
        {
            lock (_gate)
            {
                return [.. _tabs];
            }
        }
    }

    /// <summary>
    /// Gets every block in the session.
    /// </summary>
    internal IReadOnlyList<BlockInfo> Blocks
    {
        get
        {
            lock (_gate)
            {
                return [.. _blocks.Values];
            }
        }
    }

    /// <summary>
    /// Finds a block by id.
    /// </summary>
    /// <param name="id">The block id.</param>
    /// <returns>The block, or null.</returns>
    internal BlockInfo? FindBlock(string id)
    {
        lock (_gate)
        {
            return _blocks.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// Gets the active tab, if any.
    /// </summary>
    internal TabInfo? ActiveTab
    {
        get
        {
            lock (_gate)
            {
                return _tabs.Find(tab => string.Equals(tab.Id, Session.ActiveTab, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    /// Applies an event; returns the block created or closed by it, if any, with whether it was created.
    /// </summary>
    /// <param name="message">The event.</param>
    /// <returns>The affected block and whether it is new; null for other events.</returns>
    internal (BlockInfo Block, bool Created)? Apply(ProtocolMessage message)
    {
        string name = message.Event ?? string.Empty;
        lock (_gate)
        {
            switch (name)
            {
                case ProtocolEvents.SessionRenamed:
                case ProtocolEvents.SessionResized:
                    SessionInfo session = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.SessionEventData).Session;
                    if (string.Equals(session.Id, Session.Id, StringComparison.Ordinal))
                    {
                        Session = session;
                    }

                    return null;
                case ProtocolEvents.SessionClosed:
                    if (string.Equals(ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.SessionEventData).Session.Id, Session.Id, StringComparison.Ordinal))
                    {
                        Closed = true;
                    }

                    return null;
                case ProtocolEvents.ServerStopping:
                    Closed = true;
                    return null;
                case ProtocolEvents.TabCreated:
                case ProtocolEvents.TabRenamed:
                case ProtocolEvents.TabSelected:
                    TabInfo tab = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.TabEventData).Tab;
                    if (!string.Equals(tab.Session, Session.Id, StringComparison.Ordinal))
                    {
                        return null;
                    }

                    int index = _tabs.FindIndex(existing => string.Equals(existing.Id, tab.Id, StringComparison.Ordinal));
                    if (index >= 0)
                    {
                        _tabs[index] = tab;
                    }
                    else
                    {
                        _tabs.Add(tab);
                    }

                    _tabs.Sort((a, b) => a.Index.CompareTo(b.Index));
                    if (string.Equals(name, ProtocolEvents.TabSelected, StringComparison.Ordinal))
                    {
                        Session = Session with { ActiveTab = tab.Id };
                    }

                    return null;
                case ProtocolEvents.TabClosed:
                    TabInfo closed = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.TabEventData).Tab;
                    _tabs.RemoveAll(existing => string.Equals(existing.Id, closed.Id, StringComparison.Ordinal));
                    return null;
                case ProtocolEvents.BlockCreated:
                case ProtocolEvents.BlockTitled:
                case ProtocolEvents.BlockExited:
                case ProtocolEvents.BlockFocused:
                    BlockInfo block = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.BlockEventData).Block;
                    if (!string.Equals(block.Session, Session.Id, StringComparison.Ordinal))
                    {
                        return null;
                    }

                    bool created = !_blocks.ContainsKey(block.Id);
                    _blocks[block.Id] = block;
                    return (block, created);
                case ProtocolEvents.BlockClosed:
                    BlockInfo removed = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.BlockEventData).Block;
                    return _blocks.Remove(removed.Id) ? (removed, false) : null;
                case ProtocolEvents.LayoutChanged:
                    LayoutInfo layout = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.LayoutEventData).Layout;
                    if (string.Equals(layout.Tab, Session.ActiveTab, StringComparison.Ordinal))
                    {
                        Layout = layout;
                    }

                    return null;
                default:
                    return null;
            }
        }
    }
}
