using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Handlers for server, event, paste, and wait-channel methods.
/// </summary>
internal static class ServerHandlers
{
    /// <summary>
    /// Registers the handlers.
    /// </summary>
    /// <param name="dispatcher">The dispatcher.</param>
    internal static void Register(RequestDispatcher dispatcher)
    {
        dispatcher.Register(ProtocolMethods.ServerInfo, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.ServerInfoResult,
            (context, _, _) => Task.FromResult(new ServerInfoResult
            {
                Version = context.Server.Options.Version,
                Protocol = ProtocolVersion.Current,
                Pid = Environment.ProcessId,
                StartedAt = context.Server.StartedAt,
                RuntimeDirectory = context.Server.Options.RuntimeDirectory,
                Sessions = context.Registry.SessionCount,
                Blocks = context.Registry.BlockCount,
                Clients = context.Registry.ClientCount
            }));

        dispatcher.Register(ProtocolMethods.ServerShutdown, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.EmptyResult,
            (context, _, _) =>
            {
                context.Server.RequestShutdownSoon();
                return Task.FromResult(EmptyResult.Instance);
            });

        dispatcher.Register(ProtocolMethods.EventsSubscribe, ProtocolJsonContext.Default.EventsSubscribeParams, ProtocolJsonContext.Default.EmptyResult,
            (context, parameters, _) =>
            {
                context.StartEventPump(context.Registry.Events.Subscribe(parameters.Since));
                return Task.FromResult(EmptyResult.Instance);
            });

        dispatcher.Register(ProtocolMethods.PasteGet, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.PasteBuffer,
            (context, _, _) => Task.FromResult(new PasteBuffer { Text = context.Registry.PasteBuffer }));

        dispatcher.Register(ProtocolMethods.PasteSet, ProtocolJsonContext.Default.PasteBuffer, ProtocolJsonContext.Default.EmptyResult,
            (context, parameters, _) =>
            {
                context.Registry.PasteBuffer = parameters.Text;
                return Task.FromResult(EmptyResult.Instance);
            });

        dispatcher.Register(ProtocolMethods.WaitFor, ProtocolJsonContext.Default.WaitChannelParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Math.Max(1, parameters.TimeoutMs));
                await context.Server.Waits.WaitAsync(parameters.Channel, timeout.Token).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.WaitSignal, ProtocolJsonContext.Default.WaitChannelParams, ProtocolJsonContext.Default.EmptyResult,
            (context, parameters, _) =>
            {
                context.Server.Waits.Signal(parameters.Channel);
                return Task.FromResult(EmptyResult.Instance);
            });
    }
}
