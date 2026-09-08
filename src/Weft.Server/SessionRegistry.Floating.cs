using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Floating block operations of the registry.
/// </summary>
internal sealed partial class SessionRegistry
{
    /// <summary>
    /// Lifts a block out of the tiled layout to float above it.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="x">The left column, or null for the default.</param>
    /// <param name="y">The top row, or null for the default.</param>
    /// <param name="width">The width, or null for the default.</param>
    /// <param name="height">The height, or null for the default.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when blocks have been resized.</returns>
    internal Task FloatBlockAsync(Block block, int? x, int? y, int? width, int? height, CancellationToken cancellationToken)
    {
        LayoutRect defaults;
        lock (_gate)
        {
            defaults = DefaultFloatBounds(block.Tab.Session);
        }

        return FloatBlockAsync(block, new LayoutRect(x ?? defaults.X, y ?? defaults.Y, width ?? defaults.Width, height ?? defaults.Height), cancellationToken);
    }

    /// <summary>
    /// Floats a block at the given bounds, or at the default centered rectangle when null.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="bounds">The bounds, clamped to the session, or null for the default.</param>
    /// <param name="cancellationToken">Cancels the resize.</param>
    /// <returns>A task that completes when the block floats.</returns>
    internal async Task FloatBlockAsync(Block block, LayoutRect? bounds, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            Tab tab = block.Tab;
            Session session = tab.Session;
            if (!block.Floating)
            {
                tab.Layout.Remove(block.Id);
                block.Floating = true;
            }

            block.FloatingBounds = ClampBounds(session, bounds ?? DefaultFloatBounds(session));
            if (tab.Zoomed == block)
            {
                tab.Zoomed = null;
            }

            tab.Active = block;
            if (!tab.NamePinned)
            {
                tab.Name = block.DisplayTitle;
            }
            resizes = RelayoutUnsafe(tab);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        Persist(block.Tab.Session);
        _events.Publish(ProtocolEvents.BlockFocused, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
    }

    /// <summary>
    /// Returns a floating block to the tiled layout beside the active tiled block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when blocks have been resized.</returns>
    internal async Task TileBlockAsync(Block block, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            Tab tab = block.Tab;
            Session session = tab.Session;
            if (!block.Floating)
            {
                return;
            }

            block.Floating = false;
            if (tab.Layout.IsEmpty)
            {
                tab.Layout.Initialize(block.Id, session.Width, session.Height);
            }
            else
            {
                BlockId anchor = tab.Active is { Floating: false } active && tab.Layout.Find(active.Id) is not null ? active.Id : tab.Layout.Blocks[^1];
                try
                {
                    tab.Layout.Split(anchor, block.Id, SplitOrientation.LeftRight);
                }
                catch (LayoutException)
                {
                    tab.Layout.ApplyPreset(LayoutPreset.Tiled, [.. tab.Layout.Blocks, block.Id], session.Width, session.Height);
                }
            }

            tab.Active = block;
            if (!tab.NamePinned)
            {
                tab.Name = block.DisplayTitle;
            }
            resizes = RelayoutUnsafe(tab);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        Persist(block.Tab.Session);
        _events.Publish(ProtocolEvents.BlockFocused, new BlockEventData { Block = ToInfo(block) }, ProtocolJsonContext.Default.BlockEventData);
    }

    /// <summary>
    /// Moves or resizes a floating block within the session area.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="parameters">The new position and size.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when the block has been resized.</returns>
    internal async Task MoveBlockAsync(Block block, BlockMoveParams parameters, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            if (!block.Floating)
            {
                throw new ProtocolException(ErrorCodes.Unavailable, "Block " + block.Id + " is tiled; float it first.");
            }

            LayoutRect current = block.FloatingBounds;
            var requested = new LayoutRect(
                (parameters.X ?? current.X) + parameters.DeltaX,
                (parameters.Y ?? current.Y) + parameters.DeltaY,
                parameters.Width ?? current.Width,
                parameters.Height ?? current.Height);
            block.FloatingBounds = ClampBounds(block.Tab.Session, requested);
            resizes = RelayoutUnsafe(block.Tab);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        Persist(block.Tab.Session);
    }

    private LayoutRect DefaultFloatBounds(Session session)
    {
        int width = Math.Max(_options.FrameSize * 2 + 20, session.Width * 3 / 5);
        int height = Math.Max(_options.FrameSize * 2 + 5, session.Height * 3 / 5);
        return new LayoutRect((session.Width - width) / 2, (session.Height - height) / 2, width, height);
    }

    private LayoutRect ClampBounds(Session session, LayoutRect bounds)
    {
        int minimumWidth = _options.FrameSize * 2 + 2;
        int minimumHeight = _options.FrameSize * 2 + 1;
        int width = Math.Clamp(bounds.Width, minimumWidth, session.Width);
        int height = Math.Clamp(bounds.Height, minimumHeight, session.Height);
        int x = Math.Clamp(bounds.X, 0, session.Width - width);
        int y = Math.Clamp(bounds.Y, 0, session.Height - height);
        return new LayoutRect(x, y, width, height);
    }
}
