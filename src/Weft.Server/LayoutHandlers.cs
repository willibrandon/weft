using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Handlers for layout methods.
/// </summary>
internal static class LayoutHandlers
{
    /// <summary>
    /// Registers the handlers.
    /// </summary>
    /// <param name="dispatcher">The dispatcher.</param>
    internal static void Register(RequestDispatcher dispatcher)
    {
        dispatcher.Register(ProtocolMethods.LayoutGet, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.LayoutInfo,
            async (context, parameters, cancellationToken) => (context.Registry.ToLayoutInfo(await Targets.TabAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false))));

        dispatcher.Register(ProtocolMethods.LayoutApply, ProtocolJsonContext.Default.LayoutApplyParams, ProtocolJsonContext.Default.LayoutInfo,
            async (context, parameters, cancellationToken) =>
            {
                Tab tab = await Targets.TabAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.ApplyLayoutAsync(tab, parameters.Layout, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToLayoutInfo(tab);
            });

        dispatcher.Register(ProtocolMethods.LayoutPreset, ProtocolJsonContext.Default.LayoutPresetParams, ProtocolJsonContext.Default.LayoutInfo,
            async (context, parameters, cancellationToken) =>
            {
                Tab tab = await Targets.TabAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.ApplyPresetAsync(tab, parameters.Preset, parameters.MainPercent, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToLayoutInfo(tab);
            });

        dispatcher.Register(ProtocolMethods.LayoutResize, ProtocolJsonContext.Default.LayoutResizeParams, ProtocolJsonContext.Default.LayoutInfo,
            async (context, parameters, cancellationToken) =>
            {
                Block block = await Targets.BlockAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.ResizeBlockAsync(block, parameters.Direction, parameters.Amount, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToLayoutInfo(block.Tab);
            });
    }
}
