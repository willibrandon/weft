using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Block lifecycle operations of the registry.
/// </summary>
internal sealed partial class SessionRegistry
{
    private const int ActivityIntervalMs = 250;

    /// <summary>
    /// Splits a block to create a new one running a command.
    /// </summary>
    /// <param name="target">The block to split.</param>
    /// <param name="orientation">The split orientation.</param>
    /// <param name="size">The new block's size, or null for half.</param>
    /// <param name="before">Whether the new block goes before the target.</param>
    /// <param name="command">The command, or null for the default shell.</param>
    /// <param name="cwd">The working directory, or null for the target's.</param>
    /// <param name="focus">Whether the new block becomes active.</param>
    /// <param name="keepOnExit">Whether the block stays after its process exits.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The new block.</returns>
    internal Task<Block> SplitBlockAsync(Block target, SplitOrientation orientation, int? size, bool before, IReadOnlyList<string>? command, string? cwd, bool focus, bool keepOnExit, CancellationToken cancellationToken) =>
        StartBlockAsync(target.Tab, target, orientation, size, before, command, cwd ?? target.Cwd, focus, keepOnExit, cancellationToken);

    /// <summary>
    /// Closes a block, stopping its process; closing the last block closes the tab.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="cancellationToken">Cancels the wait for the process to stop.</param>
    /// <returns>A task that completes when the block is gone.</returns>
    internal async Task CloseBlockAsync(Block block, CancellationToken cancellationToken)
    {
        Tab tab = block.Tab;
        bool closeTab;
        List<PendingResize> resizes = [];
        lock (_gate)
        {
            if (!tab.Blocks.Remove(block))
            {
                return;
            }

            tab.Layout.Remove(block.Id);
            if (tab.Zoomed == block)
            {
                tab.Zoomed = null;
            }

            closeTab = tab.Blocks.Count == 0;
            if (!closeTab)
            {
                if (tab.Active == block)
                {
                    tab.Active = tab.Ordered().FirstOrDefault();
                    if (!tab.NamePinned && tab.Active is { } next)
                    {
                        tab.Name = next.DisplayTitle;
                    }
                }

                resizes.AddRange(RelayoutUnsafe(tab));
            }
        }

        await StopBlockAsync(block, cancellationToken).ConfigureAwait(false);
        _events.Publish(ProtocolEvents.BlockClosed, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
        if (closeTab)
        {
            await CloseTabAsync(tab, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        Persist(tab.Session);
        Block? active;
        lock (_gate)
        {
            active = tab.Active;
        }

        if (active is not null)
        {
            _events.Publish(ProtocolEvents.BlockFocused, new BlockEventData { Block = ToInfo(active) }, ProtocolJsonContext.Default.BlockEventData);
        }
    }

    /// <summary>
    /// Sends a signal to a block's process.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="signal">The signal number.</param>
    internal static void KillBlock(Block block, int signal)
    {
        if (block.Host is not { } host)
        {
            throw new ProtocolException(ErrorCodes.Unavailable, "Block " + block.Id + " is not running.");
        }

        host.Kill(signal);
    }

    /// <summary>
    /// Pins or unpins a block's title.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="title">The title, or null or empty to unpin.</param>
    internal void RenameBlock(Block block, string? title)
    {
        lock (_gate)
        {
            block.PinnedTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            if (!block.Tab.NamePinned && block.Tab.Active == block)
            {
                block.Tab.Name = block.DisplayTitle;
            }
        }

        Persist(block.Tab.Session);
        _events.Publish(ProtocolEvents.BlockTitled, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
    }

    /// <summary>
    /// Makes a block the active block of its tab and selects the tab.
    /// </summary>
    /// <param name="block">The block.</param>
    internal void FocusBlock(Block block)
    {
        lock (_gate)
        {
            block.Tab.Active = block;
            block.Tab.Session.ActiveTab = block.Tab;
            block.Tab.Session.LastActive = DateTimeOffset.Now;
            if (!block.Tab.NamePinned)
            {
                block.Tab.Name = block.DisplayTitle;
            }
        }

        _events.Publish(ProtocolEvents.BlockFocused, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
    }

    /// <summary>
    /// Finds the tiled neighbour of a block in a direction.
    /// </summary>
    /// <param name="block">The starting block.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The neighbour, or null.</returns>
    internal Block? Neighbor(Block block, LayoutDirection direction)
    {
        lock (_gate)
        {
            return block.Tab.Layout.FindNeighbor(block.Id, direction) is { } id ? block.Tab.Find(id) : null;
        }
    }

    /// <summary>
    /// Zooms a block to fill its tab, or restores the layout.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="zoom">Whether to zoom; null toggles.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when blocks have been resized.</returns>
    internal async Task ZoomBlockAsync(Block block, bool? zoom, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            bool target = zoom ?? block.Tab.Zoomed != block;
            block.Tab.Zoomed = target ? block : null;
            if (target)
            {
                block.Tab.Active = block;
            }

            resizes = RelayoutUnsafe(block.Tab);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Exchanges two blocks' positions in the layout.
    /// </summary>
    /// <param name="first">The first block.</param>
    /// <param name="second">The second block.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when blocks have been resized.</returns>
    internal async Task SwapBlocksAsync(Block first, Block second, CancellationToken cancellationToken)
    {
        if (first.Tab != second.Tab)
        {
            throw new ProtocolException(ErrorCodes.InvalidParams, "Blocks must be in the same tab to swap.");
        }

        List<PendingResize> resizes;
        lock (_gate)
        {
            if (!first.Tab.Layout.Swap(first.Id, second.Id))
            {
                throw new ProtocolException(ErrorCodes.Unavailable, "Both blocks must be tiled to swap.");
            }

            resizes = RelayoutUnsafe(first.Tab);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        Persist(first.Tab.Session);
    }

    /// <summary>
    /// Sends key names or text to a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="keys">The keys.</param>
    /// <param name="literal">Whether every item is literal text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    internal async Task SendKeysAsync(Block block, IReadOnlyList<string> keys, bool literal, CancellationToken cancellationToken)
    {
        BlockHost host = RunningHost(block);
        bool applicationCursorKeys = host.Capture(0, CaptureFormat.Text).ApplicationCursorKeys;
        foreach (string key in keys)
        {
            byte[] bytes = literal ? KeyEncoder.EncodeText(key) : KeyEncoder.Encode(key, applicationCursorKeys);
            await host.WriteInputAsync(bytes, cancellationToken).ConfigureAwait(false);
            await FanOutAsync(block, bytes, pasteText: null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Turns synchronized input on or off for a tab.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <param name="enabled">The new state, or null to toggle.</param>
    internal void SetTabSync(Tab tab, bool? enabled)
    {
        lock (_gate)
        {
            tab.Synchronized = enabled ?? !tab.Synchronized;
        }

        _events.Publish(ProtocolEvents.TabChanged, new TabEventData { Tab = ToInfo(tab) }, ProtocolJsonContext.Default.TabEventData);
    }

    /// <summary>
    /// Includes or excludes a block from its tab's synchronized input.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="excluded">Whether the block is excluded.</param>
    internal void SetBlockSync(Block block, bool excluded)
    {
        lock (_gate)
        {
            block.ExcludedFromSync = excluded;
        }

        _events.Publish(ProtocolEvents.BlockChanged, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
    }

    private Task FanOutAsync(Block source, ReadOnlyMemory<byte> bytes, string? pasteText, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!source.Tab.Synchronized)
            {
                return Task.CompletedTask;
            }

            List<BlockHost> targets =
            [
                .. source.Tab.Blocks
                    .Where(sibling => sibling != source && !sibling.ExcludedFromSync && sibling.Host is not null && sibling.State == BlockState.Running)
                    .Select(sibling => sibling.Host!)
            ];

            // Queue while holding the gate so the queue order matches the order input arrived in.
            return targets.Count == 0 ? Task.CompletedTask : source.Tab.SyncInput.EnqueueAsync(targets, bytes, pasteText, cancellationToken);
        }
    }

    private void OnBlockInput(Block block, ReadOnlyMemory<byte> bytes)
    {
        bool synchronized;
        lock (_gate)
        {
            synchronized = block.Tab.Synchronized;
        }

        if (synchronized)
        {
            _ = FanOutAsync(block, bytes.ToArray(), pasteText: null, CancellationToken.None);
        }
    }

    private void OnBlockOutput(Block block)
    {
        long now = Environment.TickCount64;
        lock (_gate)
        {
            if (block.State == BlockState.Closed)
            {
                return;
            }

            if (now - block.LastActivityPublished < ActivityIntervalMs)
            {
                // Output inside the quiet window is coalesced into one trailing event rather than dropped,
                // so a short burst after a client looked away still raises the activity marker.
                if (!block.ActivityTrailing)
                {
                    block.ActivityTrailing = true;
                    _ = PublishTrailingActivityAsync(block);
                }

                return;
            }

            block.LastActivityPublished = now;
        }

        _events.Publish(ProtocolEvents.BlockOutput, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
    }

    private async Task PublishTrailingActivityAsync(Block block)
    {
        await Task.Delay(ActivityIntervalMs).ConfigureAwait(false);
        lock (_gate)
        {
            block.ActivityTrailing = false;
            if (block.State == BlockState.Closed)
            {
                return;
            }

            block.LastActivityPublished = Environment.TickCount64;
        }

        _events.Publish(ProtocolEvents.BlockOutput, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
    }

    /// <summary>
    /// Types literal text into a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    internal async Task TypeAsync(Block block, string text, CancellationToken cancellationToken)
    {
        byte[] bytes = KeyEncoder.EncodeText(text);
        await RunningHost(block).WriteInputAsync(bytes, cancellationToken).ConfigureAwait(false);
        await FanOutAsync(block, bytes, pasteText: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pastes text into a block, bracketed when the terminal asked for it.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    internal async Task PasteAsync(Block block, string text, CancellationToken cancellationToken)
    {
        BlockHost host = RunningHost(block);
        bool bracketed = host.Capture(0, CaptureFormat.Text).BracketedPaste;
        await host.WriteInputAsync(bracketed ? KeyEncoder.EncodeBracketedPaste(text) : KeyEncoder.EncodeText(text), cancellationToken).ConfigureAwait(false);
        await FanOutAsync(block, ReadOnlyMemory<byte>.Empty, text, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Captures a block's screen and history.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="history">The history lines to include.</param>
    /// <param name="format">The line format.</param>
    /// <returns>The capture.</returns>
    internal static BlockCapture Capture(Block block, int history, CaptureFormat format) =>
        RunningHost(block).Capture(history, format);

    private static BlockHost RunningHost(Block block) =>
        block.Host ?? throw new ProtocolException(ErrorCodes.Unavailable, "Block " + block.Id + " is not running.");

    private async Task<Block> StartBlockAsync(
        Tab tab,
        Block? target,
        SplitOrientation orientation,
        int? size,
        bool before,
        IReadOnlyList<string>? command,
        string? cwd,
        bool focus,
        bool keepOnExit,
        CancellationToken cancellationToken)
    {
        Session session = tab.Session;
        string file = command is { Count: > 0 } ? command[0] : DefaultShell();
        IReadOnlyList<string> arguments = command is { Count: > 1 } ? command.Skip(1).ToList() : [];
        string directory = ResolveDirectory(cwd, session.Cwd);
        Block block;
        List<PendingResize> resizes;
        lock (_gate)
        {
            _nextBlock++;
            var id = new BlockId(_nextBlock);
            block = new Block(id, tab, file, arguments, directory)
            {
                SocketPath = WeftPaths.BlockSocketPath(_options.RuntimeDirectory, id),
                KeepOnExit = keepOnExit
            };
            try
            {
                if (tab.Layout.IsEmpty)
                {
                    tab.Layout.Initialize(id, session.Width, session.Height);
                }
                else if (target is not null && tab.Layout.Find(target.Id) is not null)
                {
                    tab.Layout.Split(target.Id, id, orientation, size, before);
                }
                else
                {
                    BlockId anchor = tab.Layout.Blocks[^1];
                    tab.Layout.Split(anchor, id, orientation, size, before);
                }
            }
            catch (LayoutException exception)
            {
                throw new ProtocolException(ErrorCodes.Unavailable, exception.Message);
            }

            tab.Blocks.Add(block);
            if (focus || tab.Active is null)
            {
                tab.Active = block;
                if (!tab.NamePinned)
                {
                    tab.Name = block.DisplayTitle;
                }
            }

            resizes = RelayoutUnsafe(tab);
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
            ["WEFT"] = "1",
            ["WEFT_SESSION"] = session.Name,
            ["WEFT_TAB"] = tab.Id.ToString(),
            ["WEFT_BLOCK"] = block.Id.ToString(),
            ["WEFT_SOCKET"] = WeftPaths.ControlSocketPath(_options.RuntimeDirectory)
        };
        var host = new BlockHost(block.SocketPath, file, arguments, directory, environment, Math.Max(1, block.Width), Math.Max(1, block.Height), _options.Scrollback);
        block.Host = host;
        host.Exited += code => OnBlockExited(block, code);
        host.TitleChanged += title => OnBlockTitled(block, title);
        host.InputReceived += bytes => OnBlockInput(block, bytes);
        host.Output += () => OnBlockOutput(block);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or TimeoutException)
        {
            ServerLog.Error("Could not start block " + block.Id, exception);
            lock (_gate)
            {
                block.State = BlockState.Closed;
                tab.Blocks.Remove(block);
                tab.Layout.Remove(block.Id);
                if (tab.Active == block)
                {
                    tab.Active = tab.Ordered().FirstOrDefault();
                }
            }

            await host.DisposeAsync().ConfigureAwait(false);
            throw new ProtocolException(ErrorCodes.Unavailable, "Could not start " + file + ": " + exception.Message);
        }

        lock (_gate)
        {
            if (block.State == BlockState.Starting)
            {
                block.State = BlockState.Running;
            }
        }

        _events.Publish(ProtocolEvents.BlockCreated, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
        await ApplyResizesAsync(resizes.Where(resize => resize.Host != host).ToList(), cancellationToken).ConfigureAwait(false);
        if (focus)
        {
            _events.Publish(ProtocolEvents.BlockFocused, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
        }

        Persist(session);
        return block;
    }

    private async Task StopBlockAsync(Block block, CancellationToken cancellationToken)
    {
        BlockHost? host;
        lock (_gate)
        {
            host = block.Host;
            block.Host = null;
            block.State = BlockState.Closed;
        }

        if (host is null)
        {
            return;
        }

        try
        {
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ServerLog.Warn("Block " + block.Id + " did not stop within five seconds.");
        }
    }

    private void OnBlockExited(Block block, int exitCode)
    {
        bool close;
        lock (_gate)
        {
            if (block.State == BlockState.Closed)
            {
                return;
            }

            block.State = BlockState.Exited;
            block.ExitCode = exitCode;
            close = !block.KeepOnExit;
        }

        block.StateChanged.Notify();
        _events.Publish(ProtocolEvents.BlockExited, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
        if (close)
        {
            _ = CloseExitedBlockAsync(block);
        }
    }

    private async Task CloseExitedBlockAsync(Block block)
    {
        try
        {
            await CloseBlockAsync(block, CancellationToken.None).ConfigureAwait(false);
        }
        catch (ProtocolException exception)
        {
            ServerLog.Warn("Could not close exited block " + block.Id + ": " + exception.Message);
        }
    }

    private void OnBlockTitled(Block block, string title)
    {
        bool tabRenamed = false;
        lock (_gate)
        {
            block.Title = title;
            if (!block.Tab.NamePinned && block.Tab.Active == block && !string.Equals(block.Tab.Name, block.DisplayTitle, StringComparison.Ordinal))
            {
                block.Tab.Name = block.DisplayTitle;
                tabRenamed = true;
            }
        }

        _events.Publish(ProtocolEvents.BlockTitled, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
        if (tabRenamed)
        {
            _events.Publish(ProtocolEvents.TabRenamed, new TabEventData { Tab = ToInfo(block.Tab) }, ProtocolJsonContext.Default.TabEventData);
        }
    }
}
