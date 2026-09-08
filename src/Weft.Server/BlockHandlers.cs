using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Handlers for block methods.
/// </summary>
internal static class BlockHandlers
{
    /// <summary>
    /// Registers the handlers.
    /// </summary>
    /// <param name="dispatcher">The dispatcher.</param>
    internal static void Register(RequestDispatcher dispatcher)
    {
        dispatcher.Register(ProtocolMethods.BlockList, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.BlockListResult,
            async (context, parameters, cancellationToken) =>
            {
                TargetSelector selector = Targets.Parse(parameters.Target);
                IEnumerable<Session> sessions = selector.IsEmpty ? context.Registry.ListSessions() : [await Targets.SessionAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false)];
                List<BlockInfo> blocks = [];
                foreach (Session session in sessions)
                {
                    IEnumerable<Tab> tabs = selector.Tab is null && selector.TabIndex is null ? session.Tabs : [context.Registry.ResolveTab(selector)];
                    foreach (Tab tab in tabs)
                    {
                        blocks.AddRange(tab.Ordered().Select(context.Registry.ToInfo));
                    }
                }

                return (new BlockListResult { Blocks = blocks });
            });

        dispatcher.Register(ProtocolMethods.BlockGet, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.BlockInfo,
            async (context, parameters, cancellationToken) => (context.Registry.ToInfo(await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false))));

        dispatcher.Register(ProtocolMethods.BlockSplit, ProtocolJsonContext.Default.BlockSplitParams, ProtocolJsonContext.Default.BlockInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block target = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                Block created = await context.Registry.SplitBlockAsync(target, parameters.Orientation, parameters.Size, parameters.Before, parameters.Command, parameters.Cwd, parameters.Focus, keepOnExit: false, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToInfo(created);
            });

        dispatcher.Register(ProtocolMethods.BlockClose, ProtocolJsonContext.Default.BlockCloseParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await context.Registry.CloseBlockAsync(await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockKill, ProtocolJsonContext.Default.BlockKillParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                SessionRegistry.KillBlock(await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false), parameters.Signal);
                return (EmptyResult.Instance);
            });

        dispatcher.Register(ProtocolMethods.BlockRename, ProtocolJsonContext.Default.BlockRenameParams, ProtocolJsonContext.Default.BlockInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                context.Registry.RenameBlock(block, parameters.Title);
                return (context.Registry.ToInfo(block));
            });

        dispatcher.Register(ProtocolMethods.BlockFocus, ProtocolJsonContext.Default.BlockFocusParams, ProtocolJsonContext.Default.BlockInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                if (parameters.Direction is { } direction)
                {
                    block = context.Registry.Neighbor(block, direction)
                        ?? throw new ProtocolException(ErrorCodes.NotFound, "No block " + direction.ToString().ToUpperInvariant() + " of " + block.Id + ".");
                }

                context.Registry.FocusBlock(block);
                return (context.Registry.ToInfo(block));
            });

        dispatcher.Register(ProtocolMethods.BlockZoom, ProtocolJsonContext.Default.BlockZoomParams, ProtocolJsonContext.Default.TabInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.ZoomBlockAsync(block, parameters.Zoom, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToInfo(block.Tab);
            });

        dispatcher.Register(ProtocolMethods.BlockSwap, ProtocolJsonContext.Default.BlockSwapParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                Block first = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                Block second = await Targets.BlockAsync(context.Registry, parameters.With, cancellationToken).ConfigureAwait(false);
                await context.Registry.SwapBlocksAsync(first, second, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockFloat, ProtocolJsonContext.Default.BlockFloatParams, ProtocolJsonContext.Default.LayoutInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.FloatBlockAsync(block, parameters.X, parameters.Y, parameters.Width, parameters.Height, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToLayoutInfo(block.Tab);
            });

        dispatcher.Register(ProtocolMethods.BlockTile, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.LayoutInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.TileBlockAsync(block, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToLayoutInfo(block.Tab);
            });

        dispatcher.Register(ProtocolMethods.BlockMove, ProtocolJsonContext.Default.BlockMoveParams, ProtocolJsonContext.Default.LayoutInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.MoveBlockAsync(block, parameters, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToLayoutInfo(block.Tab);
            });

        dispatcher.Register(ProtocolMethods.BlockSync, ProtocolJsonContext.Default.BlockSyncParams, ProtocolJsonContext.Default.BlockInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                context.Registry.SetBlockSync(block, parameters.Excluded);
                return context.Registry.ToInfo(block);
            });

        dispatcher.Register(ProtocolMethods.BlockSendKeys, ProtocolJsonContext.Default.BlockSendKeysParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await context.Registry.SendKeysAsync(await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false), parameters.Keys, parameters.Literal, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockType, ProtocolJsonContext.Default.BlockTextParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await SessionRegistry.TypeAsync(await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false), parameters.Text, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockPaste, ProtocolJsonContext.Default.BlockTextParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await SessionRegistry.PasteAsync(await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false), parameters.Text, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockCapture, ProtocolJsonContext.Default.BlockCaptureParams, ProtocolJsonContext.Default.BlockCaptureResult,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                BlockCapture capture = SessionRegistry.Capture(block, parameters.History, parameters.Format);
                return (new BlockCaptureResult
                {
                    Block = block.Id.ToString(),
                    Revision = capture.Revision,
                    Width = capture.Width,
                    Height = capture.Height,
                    CursorX = capture.CursorX,
                    CursorY = capture.CursorY,
                    HistoryLines = capture.HistoryLines,
                    Lines = capture.Lines
                });
            });

        dispatcher.Register(ProtocolMethods.BlockWait, ProtocolJsonContext.Default.BlockWaitParams, ProtocolJsonContext.Default.BlockWaitResult,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                return await BlockWaiter.WaitAsync(block, parameters, cancellationToken).ConfigureAwait(false);
            });

        dispatcher.Register(ProtocolMethods.BlockRun, ProtocolJsonContext.Default.BlockRunParams, ProtocolJsonContext.Default.BlockRunResult,
            async (context, parameters, cancellationToken) =>
            {
                Session session = await SessionHandlers.ResolveOrCreateAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                Tab tab = context.Registry.ResolveTab(Targets.Parse(parameters.Target) with { SessionName = session.Name, Session = null });
                return await BlockRunner.RunAsync(context.Registry, tab, parameters, cancellationToken).ConfigureAwait(false);
            });
    }
}
