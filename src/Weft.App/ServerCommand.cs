using System.CommandLine;
using System.Runtime.InteropServices;
using Weft.Client;
using Weft.Core;
using Weft.Protocol;
using Weft.Server;

namespace Weft.App;

/// <summary>
/// The server command and shutdown.
/// </summary>
internal static class ServerCommand
{
    /// <summary>
    /// Creates the server command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command Create()
    {
        var command = new Command("server", "Run the session server in this process.");
        var detached = new Option<bool>("--detached") { Description = "Run without a console, logging only to the state directory." };
        var stateDirectory = new Option<string?>("--state-dir") { Description = "Directory for persisted sessions and logs." };
        var shell = new Option<string?>("--shell") { Description = "Shell for new blocks." };
        command.Options.Add(detached);
        command.Options.Add(stateDirectory);
        command.Options.Add(shell);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = CommandContext.From(parseResult);
            await using (context.ConfigureAwait(false))
            {
                if (parseResult.GetValue(detached))
                {
                    Console.SetOut(TextWriter.Null);
                    Console.SetError(TextWriter.Null);
                }

                string state = parseResult.GetValue(stateDirectory) is { Length: > 0 } explicitState ? Path.GetFullPath(explicitState) : WeftPaths.ResolveStateDirectory();
                WeftConfig config = WeftConfigLoader.LoadDefault(out string? configError);
                if (configError is not null)
                {
                    await Console.Error.WriteLineAsync("weft: " + configError).ConfigureAwait(false);
                }

                var server = new WeftServer(new WeftServerOptions
                {
                    RuntimeDirectory = context.RuntimeDirectory,
                    StateDirectory = state,
                    DefaultShell = parseResult.GetValue(shell) ?? config.Shell,
                    FrameSize = config.Frames ? 1 : 0,
                    Scrollback = Math.Max(0, config.Scrollback),
                    DefaultWidth = Math.Clamp(config.DefaultWidth, 20, 1000),
                    DefaultHeight = Math.Clamp(config.DefaultHeight, 5, 500),
                    DefaultSizePolicy = config.SizePolicy,
                    Hooks = config.Hooks
                });
                await using (server.ConfigureAwait(false))
                {
                    using PosixSignalRegistration? hangup = OperatingSystem.IsWindows()
                        ? null
                        : PosixSignalRegistration.Create(PosixSignal.SIGHUP, signal => signal.Cancel = true);
                    try
                    {
                        await server.RunAsync(cancellationToken).ConfigureAwait(false);
                        return 0;
                    }
                    catch (InvalidOperationException exception)
                    {
                        return CommandContext.Fail(exception.Message);
                    }
                    catch (OperationCanceledException)
                    {
                        return 0;
                    }
                }
            }
        });
        return command;
    }

    /// <summary>
    /// Creates the shutdown command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateShutdown()
    {
        var command = new Command("shutdown", "Stop the server, keeping sessions on disk for resurrection.");
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeIfRunningAsync(parseResult, async (context, client) =>
        {
            await client.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => ["stopping"]);
        }, cancellationToken));
        return command;
    }
}
