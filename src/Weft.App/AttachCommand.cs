using System.CommandLine;
using Weft.Client;
using Weft.Core;

namespace Weft.App;

/// <summary>
/// The attach command: the interactive multiplexer client.
/// </summary>
internal static class AttachCommand
{
    /// <summary>
    /// Gets the read-only option.
    /// </summary>
    internal static Option<bool> ReadOnly { get; } = new("--read-only") { Description = "Watch without sending input." };

    /// <summary>
    /// Creates the attach command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command Create()
    {
        var command = new Command("attach", "Attach to a session, creating it when missing.");
        Argument<string?> target = CommonOptions.OptionalTarget("Session name or id.");
        command.Arguments.Add(target);
        command.Options.Add(ReadOnly);
        command.SetAction((parseResult, cancellationToken) => RunAsync(parseResult, parseResult.GetValue(target), cancellationToken));
        return command;
    }

    /// <summary>
    /// Runs the interactive client.
    /// </summary>
    /// <param name="parseResult">The parse result.</param>
    /// <param name="target">The session target.</param>
    /// <param name="cancellationToken">Stops the client.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> RunAsync(ParseResult parseResult, string? target, CancellationToken cancellationToken)
    {
        var context = CommandContext.From(parseResult);
        await using (context.ConfigureAwait(false))
        {
            if (Environment.GetEnvironmentVariable("WEFT") is "1" && string.IsNullOrEmpty(target))
            {
                return CommandContext.Fail("already inside a weft session; name a session to attach to it from here.");
            }

            ControlClient probe;
            try
            {
                probe = await context.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                return CommandContext.Fail(exception.Message);
            }

            _ = probe;
            WeftConfig config = WeftConfigLoader.LoadDefault(out string? configError);
            if (configError is not null)
            {
                await Console.Error.WriteLineAsync("weft: " + configError).ConfigureAwait(false);
            }

            string? current = string.IsNullOrEmpty(target) ? null : target;
            while (true)
            {
                var app = new AttachApp(new AttachOptions
                {
                    SocketPath = context.SocketPath,
                    Target = current,
                    ReadOnly = parseResult.GetValue(ReadOnly),
                    Config = config
                });
                await app.RunAsync(cancellationToken).ConfigureAwait(false);
                if (app.ExitMessage is { } message)
                {
                    await Console.Error.WriteLineAsync("weft: " + message).ConfigureAwait(false);
                }

                if (app.SwitchTarget is not { } next)
                {
                    return 0;
                }

                current = next.Length == 0 ? null : next;
                if (current is null)
                {
                    ControlClient client = await context.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    Protocol.SessionInfo created = await client.CreateSessionAsync(new Protocol.SessionCreateParams(), cancellationToken).ConfigureAwait(false);
                    current = created.Name;
                }
            }
        }
    }
}
