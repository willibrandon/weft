using System.CommandLine;
using Weft.Client;
using Weft.Protocol;

namespace Weft.App;

/// <summary>
/// Event streaming and wait-channel commands.
/// </summary>
internal static class EventCommands
{
    /// <summary>
    /// Creates the events command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateEvents()
    {
        var command = new Command("events", "Stream server events as JSON lines until interrupted.");
        var since = new Option<long?>("--since") { Description = "Replay events after this sequence number." };
        command.Options.Add(since);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (_, client) =>
        {
            await client.SubscribeAsync(parseResult.GetValue(since), cancellationToken).ConfigureAwait(false);
            await foreach (ProtocolMessage message in client.Events.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await Console.Out.WriteLineAsync(ProtocolCodec.ToJson(message)).ConfigureAwait(false);
            }
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates the signal command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateSignal()
    {
        var command = new Command("signal", "Signal a named wait channel.");
        var channel = new Argument<string>("channel") { Description = "Channel name." };
        command.Arguments.Add(channel);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            await client.SignalAsync(parseResult.GetValue(channel) ?? string.Empty, cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => []);
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates the wait-for command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateWaitFor()
    {
        var command = new Command("wait-for", "Block until a named channel is signalled.");
        var channel = new Argument<string>("channel") { Description = "Channel name." };
        var timeout = new Option<int>("--timeout") { Description = "Timeout in milliseconds.", DefaultValueFactory = _ => 30_000 };
        command.Arguments.Add(channel);
        command.Options.Add(timeout);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            await client.WaitForAsync(new WaitChannelParams { Channel = parseResult.GetValue(channel) ?? string.Empty, TimeoutMs = parseResult.GetValue(timeout) }, cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => []);
        }, cancellationToken));
        return command;
    }
}
