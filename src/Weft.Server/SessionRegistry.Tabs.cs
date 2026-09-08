using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Tab operations of the registry.
/// </summary>
internal sealed partial class SessionRegistry
{
    /// <summary>
    /// Creates a tab with one block and selects it.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="name">The name, or null to follow the block's title.</param>
    /// <param name="cwd">The working directory, or null for the session's.</param>
    /// <param name="command">The command, or null for the default shell.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The tab.</returns>
    internal async Task<Tab> CreateTabAsync(Session session, string? name, string? cwd, IReadOnlyList<string>? command, CancellationToken cancellationToken)
    {
        Tab tab;
        lock (_gate)
        {
            _nextTab++;
            tab = new Tab(new TabId(_nextTab), session, name ?? string.Empty, LayoutOptionsFor()) { NamePinned = !string.IsNullOrEmpty(name) };
            session.Tabs.Add(tab);
            session.ActiveTab ??= tab;
        }

        try
        {
            await StartBlockAsync(tab, null, SplitOrientation.LeftRight, null, false, command, cwd, focus: true, keepOnExit: false, cancellationToken).ConfigureAwait(false);
        }
        catch (ProtocolException)
        {
            lock (_gate)
            {
                session.Tabs.Remove(tab);
                if (session.ActiveTab == tab)
                {
                    session.ActiveTab = session.Tabs.FirstOrDefault();
                }
            }

            throw;
        }

        _events.Publish(ProtocolEvents.TabCreated, new TabEventData { Tab = ToInfo(tab) }, ProtocolJsonContext.Default.TabEventData);
        SelectTab(tab);
        Persist(session);
        return tab;
    }

    /// <summary>
    /// Makes a tab the active tab of its session.
    /// </summary>
    /// <param name="tab">The tab.</param>
    internal void SelectTab(Tab tab)
    {
        lock (_gate)
        {
            tab.Session.ActiveTab = tab;
            tab.Session.LastActive = DateTimeOffset.Now;
        }

        _events.Publish(ProtocolEvents.TabSelected, new TabEventData { Tab = ToInfo(tab) }, ProtocolJsonContext.Default.TabEventData);
        _events.Publish(ProtocolEvents.LayoutChanged, new LayoutEventData { Layout = ToLayoutInfo(tab) }, ProtocolJsonContext.Default.LayoutEventData);
    }

    /// <summary>
    /// Renames a tab and pins the name.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <param name="name">The name; empty unpins and follows the active block's title again.</param>
    internal void RenameTab(Tab tab, string name)
    {
        lock (_gate)
        {
            tab.NamePinned = !string.IsNullOrWhiteSpace(name);
            tab.Name = tab.NamePinned ? name.Trim() : tab.Active?.DisplayTitle ?? string.Empty;
        }

        Persist(tab.Session);
        _events.Publish(ProtocolEvents.TabRenamed, new TabEventData { Tab = ToInfo(tab) }, ProtocolJsonContext.Default.TabEventData);
    }

    /// <summary>
    /// Closes a tab and every block in it; closing the last tab closes the session.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <param name="cancellationToken">Cancels the wait for blocks to stop.</param>
    /// <returns>A task that completes when the tab is gone.</returns>
    internal Task CloseTabAsync(Tab tab, CancellationToken cancellationToken) => CloseTabAsync(tab, closingSession: false, cancellationToken);

    private async Task CloseTabAsync(Tab tab, bool closingSession, CancellationToken cancellationToken)
    {
        Session session = tab.Session;
        List<Block> blocks;
        bool closeSession;
        lock (_gate)
        {
            if (!session.Tabs.Remove(tab))
            {
                return;
            }

            blocks = [.. tab.Blocks];
            if (session.ActiveTab == tab)
            {
                session.ActiveTab = session.Tabs.FirstOrDefault();
            }

            closeSession = !closingSession && session.Tabs.Count == 0;
        }

        foreach (Block block in blocks)
        {
            await StopBlockAsync(block, cancellationToken).ConfigureAwait(false);
        }

        _events.Publish(ProtocolEvents.TabClosed, new TabEventData { Tab = ToInfo(tab) }, ProtocolJsonContext.Default.TabEventData);
        if (closeSession)
        {
            await CloseSessionAsync(session, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!closingSession)
        {
            Persist(session);
            Tab? active;
            lock (_gate)
            {
                active = session.ActiveTab;
            }

            if (active is not null)
            {
                _events.Publish(ProtocolEvents.TabSelected, new TabEventData { Tab = ToInfo(active) }, ProtocolJsonContext.Default.TabEventData);
                _events.Publish(ProtocolEvents.LayoutChanged, new LayoutEventData { Layout = ToLayoutInfo(active) }, ProtocolJsonContext.Default.LayoutEventData);
            }
        }
    }

    private async Task RestoreTabAsync(Session session, StoredTab stored, CancellationToken cancellationToken)
    {
        if (stored.Blocks.Count == 0)
        {
            return;
        }

        Tab tab;
        lock (_gate)
        {
            _nextTab++;
            tab = new Tab(new TabId(_nextTab), session, stored.Name, LayoutOptionsFor()) { NamePinned = stored.NamePinned };
            session.Tabs.Add(tab);
            session.ActiveTab ??= tab;
        }

        Block? first = null;
        Dictionary<int, Block> restored = [];
        foreach (StoredBlock storedBlock in stored.Blocks.Where(block => !block.Floating))
        {
            try
            {
                Block block = await StartBlockAsync(tab, first, SplitOrientation.TopBottom, null, false, [storedBlock.Command, .. storedBlock.Args], storedBlock.Cwd, focus: first is null, keepOnExit: false, cancellationToken).ConfigureAwait(false);
                block.PinnedTitle = storedBlock.Title;
                restored[storedBlock.Id] = block;
                first ??= block;
            }
            catch (ProtocolException exception)
            {
                ServerLog.Warn("Could not restore block " + storedBlock.Command + ": " + exception.Message);
            }
        }

        if (first is null)
        {
            lock (_gate)
            {
                session.Tabs.Remove(tab);
                if (session.ActiveTab == tab)
                {
                    session.ActiveTab = null;
                }
            }

            return;
        }

        foreach (StoredBlock storedBlock in stored.Blocks.Where(block => block.Floating))
        {
            try
            {
                Block block = await StartBlockAsync(tab, first, SplitOrientation.TopBottom, null, false, [storedBlock.Command, .. storedBlock.Args], storedBlock.Cwd, focus: false, keepOnExit: false, cancellationToken).ConfigureAwait(false);
                block.PinnedTitle = storedBlock.Title;
                await FloatBlockAsync(block, new LayoutRect(storedBlock.X, storedBlock.Y, storedBlock.Width, storedBlock.Height), cancellationToken).ConfigureAwait(false);
                restored[storedBlock.Id] = block;
            }
            catch (ProtocolException exception)
            {
                ServerLog.Warn("Could not restore floating block " + storedBlock.Command + ": " + exception.Message);
            }
        }

        List<PendingResize> resizes = [];
        lock (_gate)
        {
            if (LayoutSerializer.TryParse(stored.Layout, out LayoutCell? root) && root is not null)
            {
                List<BlockId> order = [];
                CollectStoredLeaves(root, restored, order);
                if (order.Count == tab.Layout.Blocks.Count)
                {
                    tab.Layout.TryApply(root, order, session.Width, session.Height);
                }
            }

            if (stored.ActiveBlock is { } activeId && restored.TryGetValue(activeId, out Block? active))
            {
                tab.Active = active;
            }

            resizes.AddRange(RelayoutUnsafe(tab));
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        _events.Publish(ProtocolEvents.TabCreated, new TabEventData { Tab = ToInfo(tab) }, ProtocolJsonContext.Default.TabEventData);
    }

    private static void CollectStoredLeaves(LayoutCell cell, Dictionary<int, Block> restored, List<BlockId> order)
    {
        if (cell.IsLeaf)
        {
            if (cell.Block is { } stored && restored.TryGetValue(stored.Value, out Block? block))
            {
                order.Add(block.Id);
                cell.AssignBlock(block.Id);
            }

            return;
        }

        foreach (LayoutCell child in cell.Children)
        {
            CollectStoredLeaves(child, restored, order);
        }
    }
}
