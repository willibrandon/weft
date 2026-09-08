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
            (context, parameters, _) =>
            {
                TargetSelector selector = Targets.Parse(parameters.Target);
                IEnumerable<Session> sessions = selector.IsEmpty ? context.Registry.ListSessions() : [context.Registry.ResolveSession(selector)];
                List<BlockInfo> blocks = [];
                foreach (Session session in sessions)
                {
                    IEnumerable<Tab> tabs = selector.Tab is null && selector.TabIndex is null ? session.Tabs : [context.Registry.ResolveTab(selector)];
                    foreach (Tab tab in tabs)
                    {
                        blocks.AddRange(tab.Ordered().Select(context.Registry.ToInfo));
                    }
                }

                return ValueTask.FromResult(new BlockListResult { Blocks = blocks });
            });

        dispatcher.Register(ProtocolMethods.BlockGet, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.BlockInfo,
            (context, parameters, _) => ValueTask.FromResult(context.Registry.ToInfo(context.Registry.ResolveBlock(Targets.Parse(parameters.Target)))));

        dispatcher.Register(ProtocolMethods.BlockSplit, ProtocolJsonContext.Default.BlockSplitParams, ProtocolJsonContext.Default.BlockInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block target = context.Registry.ResolveBlock(Targets.Parse(parameters.Target));
                Block created = await context.Registry.SplitBlockAsync(target, parameters.Orientation, parameters.Size, parameters.Before, parameters.Command, parameters.Cwd, parameters.Focus, keepOnExit: false, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToInfo(created);
            });

        dispatcher.Register(ProtocolMethods.BlockClose, ProtocolJsonContext.Default.BlockCloseParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await context.Registry.CloseBlockAsync(context.Registry.ResolveBlock(Targets.Parse(parameters.Target)), cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockKill, ProtocolJsonContext.Default.BlockKillParams, ProtocolJsonContext.Default.EmptyResult,
            (context, parameters, _) =>
            {
                SessionRegistry.KillBlock(context.Registry.ResolveBlock(Targets.Parse(parameters.Target)), parameters.Signal);
                return ValueTask.FromResult(EmptyResult.Instance);
            });

        dispatcher.Register(ProtocolMethods.BlockRename, ProtocolJsonContext.Default.BlockRenameParams, ProtocolJsonContext.Default.BlockInfo,
            (context, parameters, _) =>
            {
                Block block = context.Registry.ResolveBlock(Targets.Parse(parameters.Target));
                context.Registry.RenameBlock(block, parameters.Title);
                return ValueTask.FromResult(context.Registry.ToInfo(block));
            });

        dispatcher.Register(ProtocolMethods.BlockFocus, ProtocolJsonContext.Default.BlockFocusParams, ProtocolJsonContext.Default.BlockInfo,
            (context, parameters, _) =>
            {
                Block block = context.Registry.ResolveBlock(Targets.Parse(parameters.Target));
                if (parameters.Direction is { } direction)
                {
                    block = context.Registry.Neighbor(block, direction)
                        ?? throw new ProtocolException(ErrorCodes.NotFound, "No block " + direction.ToString().ToUpperInvariant() + " of " + block.Id + ".");
                }

                context.Registry.FocusBlock(block);
                return ValueTask.FromResult(context.Registry.ToInfo(block));
            });

        dispatcher.Register(ProtocolMethods.BlockZoom, ProtocolJsonContext.Default.BlockZoomParams, ProtocolJsonContext.Default.TabInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = context.Registry.ResolveBlock(Targets.Parse(parameters.Target));
                await context.Registry.ZoomBlockAsync(block, parameters.Zoom, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToInfo(block.Tab);
            });

        dispatcher.Register(ProtocolMethods.BlockSwap, ProtocolJsonContext.Default.BlockSwapParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                Block first = context.Registry.ResolveBlock(Targets.Parse(parameters.Target));
                Block second = context.Registry.ResolveBlock(Targets.Parse(parameters.With));
                await context.Registry.SwapBlocksAsync(first, second, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockSendKeys, ProtocolJsonContext.Default.BlockSendKeysParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await SessionRegistry.SendKeysAsync(context.Registry.ResolveBlock(Targets.Parse(parameters.Target)), parameters.Keys, parameters.Literal, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockType, ProtocolJsonContext.Default.BlockTextParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await SessionRegistry.TypeAsync(context.Registry.ResolveBlock(Targets.Parse(parameters.Target)), parameters.Text, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockPaste, ProtocolJsonContext.Default.BlockTextParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await SessionRegistry.PasteAsync(context.Registry.ResolveBlock(Targets.Parse(parameters.Target)), parameters.Text, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.BlockCapture, ProtocolJsonContext.Default.BlockCaptureParams, ProtocolJsonContext.Default.BlockCaptureResult,
            (context, parameters, _) =>
            {
                Block block = context.Registry.ResolveBlock(Targets.Parse(parameters.Target));
                BlockCapture capture = SessionRegistry.Capture(block, parameters.History, parameters.Format);
                return ValueTask.FromResult(new BlockCaptureResult
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
            (context, parameters, cancellationToken) =>
                BlockWaiter.WaitAsync(context.Registry.ResolveBlock(Targets.Parse(parameters.Target)), parameters, cancellationToken));

        dispatcher.Register(ProtocolMethods.BlockRun, ProtocolJsonContext.Default.BlockRunParams, ProtocolJsonContext.Default.BlockRunResult,
            async (context, parameters, cancellationToken) =>
            {
                Session session = await SessionHandlers.ResolveOrCreateAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                Tab tab = context.Registry.ResolveTab(Targets.Parse(parameters.Target) with { SessionName = session.Name, Session = null });
                return await BlockRunner.RunAsync(context.Registry, tab, parameters, cancellationToken).ConfigureAwait(false);
            });
    }
}
