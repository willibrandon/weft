using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Owns every session, tab, block, and attached client; serializes mutations; publishes events.
/// </summary>
internal sealed partial class SessionRegistry
{
    private readonly Lock _gate = new();
    private readonly WeftServerOptions _options;
    private readonly SessionStore _store;
    private readonly List<Session> _sessions = [];
    private int _nextSession;
    private int _nextTab;
    private int _nextBlock;
    private int _nextClient;

    /// <summary>
    /// Initializes an empty registry.
    /// </summary>
    /// <param name="options">The server options.</param>
    /// <param name="events">The event log.</param>
    /// <param name="store">The session store.</param>
    internal SessionRegistry(WeftServerOptions options, EventLog events, SessionStore store)
    {
        _options = options;
        Events = events;
        _store = store;
    }

    /// <summary>
    /// Gets or sets the server-wide paste buffer.
    /// </summary>
    internal string PasteBuffer { get; set; } = string.Empty;

    /// <summary>
    /// Gets the event log.
    /// </summary>
    internal EventLog Events { get; }

    /// <summary>
    /// Gets the number of sessions.
    /// </summary>
    internal int SessionCount
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of blocks across sessions.
    /// </summary>
    internal int BlockCount
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Sum(session => session.BlockCount);
            }
        }
    }

    /// <summary>
    /// Gets the number of attached clients across sessions.
    /// </summary>
    internal int ClientCount
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Sum(session => session.Clients.Count);
            }
        }
    }

    /// <summary>
    /// Lists sessions in creation order.
    /// </summary>
    /// <returns>The sessions.</returns>
    internal IReadOnlyList<Session> ListSessions()
    {
        lock (_gate)
        {
            return [.. _sessions];
        }
    }

    /// <summary>
    /// Resolves a selector to a session, defaulting to the most recently active one.
    /// </summary>
    /// <param name="selector">The selector.</param>
    /// <returns>The session.</returns>
    /// <exception cref="ProtocolException">No session matches.</exception>
    internal Session ResolveSession(TargetSelector selector)
    {
        lock (_gate)
        {
            return ResolveSessionUnsafe(selector);
        }
    }

    /// <summary>
    /// Resolves a selector to a tab, defaulting to the session's active tab.
    /// </summary>
    /// <param name="selector">The selector.</param>
    /// <returns>The tab.</returns>
    /// <exception cref="ProtocolException">No tab matches.</exception>
    internal Tab ResolveTab(TargetSelector selector)
    {
        lock (_gate)
        {
            return ResolveTabUnsafe(selector);
        }
    }

    /// <summary>
    /// Resolves a selector to a block, defaulting to the tab's active block.
    /// </summary>
    /// <param name="selector">The selector.</param>
    /// <returns>The block.</returns>
    /// <exception cref="ProtocolException">No block matches.</exception>
    internal Block ResolveBlock(TargetSelector selector)
    {
        lock (_gate)
        {
            return ResolveBlockUnsafe(selector);
        }
    }

    /// <summary>
    /// Finds an attached client by id.
    /// </summary>
    /// <param name="clientId">The client id.</param>
    /// <returns>The client.</returns>
    /// <exception cref="ProtocolException">No client matches.</exception>
    internal AttachedClient ResolveClient(string clientId)
    {
        lock (_gate)
        {
            return _sessions.Select(session => session.Clients.Find(client => string.Equals(client.Id, clientId, StringComparison.Ordinal))).FirstOrDefault(client => client is not null)
                ?? throw new ProtocolException(ErrorCodes.NotFound, "No client " + clientId + " is attached.");
        }
    }

    private Session ResolveSessionUnsafe(TargetSelector selector)
    {
        return selector switch
        {
            { Session: { } id } => _sessions.Find(session => session.Id == id)
                ?? throw new ProtocolException(ErrorCodes.NotFound, "No session " + id + "."),
            { SessionName: { } name } => _sessions.Find(session => string.Equals(session.Name, name, StringComparison.Ordinal))
                ?? throw new ProtocolException(ErrorCodes.NotFound, "No session named " + name + "."),
            { Tab: { } tabId } => _sessions.Find(session => session.FindTab(tabId) is not null)
                ?? throw new ProtocolException(ErrorCodes.NotFound, "No tab " + tabId + "."),
            { Block: { } blockId } => _sessions.Find(session => session.FindBlock(blockId) is not null)
                ?? throw new ProtocolException(ErrorCodes.NotFound, "No block " + blockId + "."),
            _ => _sessions.OrderByDescending(session => session.LastActive).FirstOrDefault()
                ?? throw new ProtocolException(ErrorCodes.NotFound, "There are no sessions.")
        };
    }

    private Tab ResolveTabUnsafe(TargetSelector selector)
    {
        Session session = ResolveSessionUnsafe(selector);
        return selector switch
        {
            { Tab: { } tabId } => session.FindTab(tabId)
                ?? throw new ProtocolException(ErrorCodes.NotFound, "No tab " + tabId + "."),
            { TabIndex: { } index } => index >= 1 && index <= session.Tabs.Count
                ? session.Tabs[index - 1]
                : throw new ProtocolException(ErrorCodes.NotFound, "Session " + session.Name + " has no tab " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."),
            { Block: { } blockId } => session.FindBlock(blockId)?.Tab
                ?? throw new ProtocolException(ErrorCodes.NotFound, "No block " + blockId + "."),
            _ => session.ActiveTab ?? throw new ProtocolException(ErrorCodes.NotFound, "Session " + session.Name + " has no tabs.")
        };
    }

    private Block ResolveBlockUnsafe(TargetSelector selector)
    {
        if (selector.Block is { } blockId)
        {
            return _sessions.Select(session => session.FindBlock(blockId)).FirstOrDefault(block => block is not null)
                ?? throw new ProtocolException(ErrorCodes.NotFound, "No block " + blockId + ".");
        }

        Tab tab = ResolveTabUnsafe(selector);
        if (selector.BlockIndex is { } index)
        {
            List<Block> ordered = tab.Ordered();
            return index >= 1 && index <= ordered.Count
                ? ordered[index - 1]
                : throw new ProtocolException(ErrorCodes.NotFound, "Tab " + tab.Id + " has no block " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        return tab.Active ?? throw new ProtocolException(ErrorCodes.NotFound, "Tab " + tab.Id + " has no blocks.");
    }

    private LayoutOptions LayoutOptionsFor()
    {
        return _options.FrameSize > 0 ? LayoutOptions.Framed : LayoutOptions.Separated;
    }

    private string DefaultShell()
    {
        return !string.IsNullOrEmpty(_options.DefaultShell)
            ? _options.DefaultShell
            : OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("COMSPEC") is { Length: > 0 } comspec ? comspec : "cmd.exe"
            : Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell ? shell : "/bin/sh";
    }

    private string NextSessionName()
    {
        if (!_sessions.Exists(session => string.Equals(session.Name, "main", StringComparison.Ordinal)))
        {
            return "main";
        }

        int number = 1;
        while (_sessions.Exists(session => string.Equals(session.Name, number.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)))
        {
            number++;
        }

        return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
