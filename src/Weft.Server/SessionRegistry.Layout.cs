using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Layout operations and geometry of the registry.
/// </summary>
internal sealed partial class SessionRegistry
{
    /// <summary>
    /// Rebuilds a tab's layout into a preset, or the next preset when none is given.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <param name="preset">The preset, or null to cycle.</param>
    /// <param name="mainPercent">The main block's share for main presets.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when blocks have been resized.</returns>
    internal async Task ApplyPresetAsync(Tab tab, LayoutPreset? preset, int mainPercent, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            LayoutPreset chosen = preset ?? (LayoutPreset)(((int)tab.LastPreset + 1) % 5);
            List<BlockId> blocks = [.. tab.Layout.Blocks];
            if (tab.Active is { } active && blocks.Remove(active.Id))
            {
                blocks.Insert(0, active.Id);
            }

            tab.Layout.ApplyPreset(chosen, blocks, tab.Session.Width, tab.Session.Height, mainPercent);
            tab.LastPreset = chosen;
            tab.Zoomed = null;
            resizes = RelayoutUnsafe(tab);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        Persist(tab.Session);
    }

    /// <summary>
    /// Applies a serialized layout to a tab.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <param name="layout">The layout string.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when blocks have been resized.</returns>
    internal async Task ApplyLayoutAsync(Tab tab, string layout, CancellationToken cancellationToken)
    {
        if (!LayoutSerializer.TryParse(layout, out LayoutCell? root) || root is null)
        {
            throw new ProtocolException(ErrorCodes.InvalidParams, "The layout string is malformed or its checksum does not match.");
        }

        List<PendingResize> resizes;
        lock (_gate)
        {
            if (!tab.Layout.TryApply(root, tab.Layout.Blocks, tab.Session.Width, tab.Session.Height))
            {
                throw new ProtocolException(ErrorCodes.InvalidParams, "The layout has a different number of blocks than the tab.");
            }

            tab.Zoomed = null;
            resizes = RelayoutUnsafe(tab);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        Persist(tab.Session);
    }

    /// <summary>
    /// Moves one edge of a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="direction">The edge.</param>
    /// <param name="amount">The number of cells.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>Whether any change was possible.</returns>
    internal async Task<bool> ResizeBlockAsync(Block block, LayoutDirection direction, int amount, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            if (!block.Tab.Layout.Resize(block.Id, direction, amount))
            {
                return false;
            }

            block.Tab.Zoomed = null;
            resizes = RelayoutUnsafe(block.Tab);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        Persist(block.Tab.Session);
        return true;
    }

    private List<PendingResize> RelayoutUnsafe(Tab tab)
    {
        Session session = tab.Session;
        tab.Layout.Fit(session.Width, session.Height);
        int frame = _options.FrameSize;
        List<PendingResize> resizes = [];
        foreach (Block block in tab.Blocks)
        {
            LayoutRect bounds;
            if (tab.Zoomed == block)
            {
                bounds = new LayoutRect(0, 0, session.Width, session.Height);
            }
            else if (block.Floating)
            {
                // The session may have shrunk since the block was floated; keep it inside the new size.
                bounds = ClampBounds(session, block.FloatingBounds);
                block.FloatingBounds = bounds;
            }
            else if (tab.Layout.Find(block.Id) is { } cell)
            {
                bounds = cell.Bounds;
            }
            else
            {
                continue;
            }

            int width = Math.Max(1, bounds.Width - 2 * frame);
            int height = Math.Max(1, bounds.Height - 2 * frame);
            if (width != block.Width || height != block.Height)
            {
                block.Width = width;
                block.Height = height;
                if (block.Host is { } host)
                {
                    resizes.Add(new PendingResize(host, width, height));
                }
            }
        }

        _events.Publish(ProtocolEvents.LayoutChanged, new LayoutEventData { Layout = ToLayoutInfoUnsafe(tab) }, ProtocolJsonContext.Default.LayoutEventData);
        return resizes;
    }

    private static async Task ApplyResizesAsync(IReadOnlyList<PendingResize> resizes, CancellationToken cancellationToken)
    {
        foreach (PendingResize resize in resizes)
        {
            try
            {
                await resize.Host.ResizeAsync(resize.Width, resize.Height, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                ServerLog.Warn("Could not resize a block: " + exception.Message);
            }
            catch (ObjectDisposedException)
            {
                ServerLog.Debug("ApplyResizesAsync ignored ObjectDisposedException.");
            }
        }
    }
}
