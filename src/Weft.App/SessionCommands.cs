using System.CommandLine;
using System.Globalization;
using Weft.Client;
using Weft.Protocol;

namespace Weft.App;

/// <summary>
/// Session, tab, and listing commands.
/// </summary>
internal static class SessionCommands
{
    /// <summary>
    /// Creates the new command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateNew()
    {
        var command = new Command("new", "Create a session.");
        var name = new Argument<string?>("name") { Description = "Session name.", Arity = ArgumentArity.ZeroOrOne };
        var cwd = new Option<string?>("--cwd") { Description = "Working directory for the first block." };
        var width = new Option<int?>("--width") { Description = "Initial width in columns." };
        var height = new Option<int?>("--height") { Description = "Initial height in rows." };
        Argument<string[]> run = CommonOptions.Command();
        command.Arguments.Add(name);
        command.Options.Add(cwd);
        command.Options.Add(width);
        command.Options.Add(height);
        command.Arguments.Add(run);
        command.SetAction((parseResult, cancellationToken) => InvokeAsync(parseResult, async (context, client) =>
        {
            string[] arguments = parseResult.GetValue(run) ?? [];
            SessionInfo session = await client.CreateSessionAsync(new SessionCreateParams
            {
                Name = parseResult.GetValue(name),
                Cwd = parseResult.GetValue(cwd),
                Width = parseResult.GetValue(width),
                Height = parseResult.GetValue(height),
                Command = arguments.Length > 0 ? arguments : null
            }, cancellationToken).ConfigureAwait(false);
            context.Write(session, ProtocolJsonContext.Default.SessionInfo, value => [FormatSession(value)]);
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates the ls command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateList()
    {
        var command = new Command("ls", "List sessions.");
        command.SetAction((parseResult, cancellationToken) => InvokeIfRunningAsync(parseResult, async (context, client) =>
        {
            SessionListResult result = await client.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
            context.Write(result, ProtocolJsonContext.Default.SessionListResult, value => value.Sessions.Count == 0 ? ["no sessions"] : value.Sessions.Select(FormatSession));
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates the kill-session command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateKillSession()
    {
        var command = new Command("kill-session", "Close a session and every block in it.");
        Argument<string?> target = CommonOptions.OptionalTarget("Session name or id.");
        command.Arguments.Add(target);
        command.SetAction((parseResult, cancellationToken) => InvokeAsync(parseResult, async (context, client) =>
        {
            await client.CloseSessionAsync(parseResult.GetValue(target), cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => []);
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates the info command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateInfo()
    {
        var command = new Command("info", "Show server information.");
        command.SetAction((parseResult, cancellationToken) => InvokeIfRunningAsync(parseResult, async (context, client) =>
        {
            ServerInfoResult info = await client.ServerInfoAsync(cancellationToken).ConfigureAwait(false);
            context.Write(info, ProtocolJsonContext.Default.ServerInfoResult, value =>
            [
                "version   " + value.Version,
                "protocol  " + value.Protocol.ToString(CultureInfo.InvariantCulture),
                "pid       " + value.Pid.ToString(CultureInfo.InvariantCulture),
                "started   " + value.StartedAt.ToString("u", CultureInfo.InvariantCulture),
                "runtime   " + value.RuntimeDirectory,
                "sessions  " + value.Sessions.ToString(CultureInfo.InvariantCulture),
                "blocks    " + value.Blocks.ToString(CultureInfo.InvariantCulture),
                "clients   " + value.Clients.ToString(CultureInfo.InvariantCulture)
            ]);
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates the tabs command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateTabs()
    {
        var command = new Command("tabs", "List a session's tabs.");
        Argument<string?> target = CommonOptions.OptionalTarget("Session name or id.");
        command.Arguments.Add(target);
        command.SetAction((parseResult, cancellationToken) => InvokeAsync(parseResult, async (context, client) =>
        {
            TabListResult result = await client.ListTabsAsync(CommonOptions.EffectiveTarget(parseResult.GetValue(target)), cancellationToken).ConfigureAwait(false);
            context.Write(result, ProtocolJsonContext.Default.TabListResult, value => value.Tabs.Select(tab =>
                (tab.Active ? "* " : "  ") + tab.Id + "  " + tab.Index.ToString(CultureInfo.InvariantCulture) + ": " + tab.Name + "  (" + tab.Blocks.ToString(CultureInfo.InvariantCulture) + " blocks)"));
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates the blocks command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateBlocks()
    {
        var command = new Command("blocks", "List blocks.");
        Argument<string?> target = CommonOptions.OptionalTarget("Session or tab; all sessions when omitted.");
        command.Arguments.Add(target);
        command.SetAction((parseResult, cancellationToken) => InvokeAsync(parseResult, async (context, client) =>
        {
            BlockListResult result = await client.ListBlocksAsync(parseResult.GetValue(target), cancellationToken).ConfigureAwait(false);
            context.Write(result, ProtocolJsonContext.Default.BlockListResult, value => value.Blocks.Select(FormatBlock));
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates the sync command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command CreateSync()
    {
        var command = new Command("sync", "Toggle synchronized input for a tab, or exclude a block from it.");
        Argument<string?> target = CommonOptions.OptionalTarget("Tab, or block with --exclude or --include.");
        var on = new Option<bool>("--on") { Description = "Turn synchronized input on." };
        var off = new Option<bool>("--off") { Description = "Turn synchronized input off." };
        var exclude = new Option<bool>("--exclude") { Description = "Exclude the target block from its tab's synchronized input." };
        var include = new Option<bool>("--include") { Description = "Include the target block again." };
        command.Arguments.Add(target);
        command.Options.Add(on);
        command.Options.Add(off);
        command.Options.Add(exclude);
        command.Options.Add(include);
        command.SetAction((parseResult, cancellationToken) => InvokeAsync(parseResult, async (context, client) =>
        {
            string? effective = CommonOptions.EffectiveTarget(parseResult.GetValue(target));
            if (parseResult.GetValue(exclude) || parseResult.GetValue(include))
            {
                BlockInfo block = await client.SyncBlockAsync(new BlockSyncParams { Target = effective, Excluded = parseResult.GetValue(exclude) }, cancellationToken).ConfigureAwait(false);
                context.Write(block, ProtocolJsonContext.Default.BlockInfo, value => [value.Id + (value.ExcludedFromSync ? "  excluded" : "  included")]);
                return;
            }

            bool? enabled = parseResult.GetValue(on) ? true : parseResult.GetValue(off) ? false : null;
            TabInfo tab = await client.SyncTabAsync(new TabSyncParams { Target = effective, Enabled = enabled }, cancellationToken).ConfigureAwait(false);
            context.Write(tab, ProtocolJsonContext.Default.TabInfo, value => [value.Id + (value.Synchronized ? "  synchronized" : "  independent")]);
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Formats a session as one line.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>The line.</returns>
    internal static string FormatSession(SessionInfo session) =>
        session.Name + "  " + session.Id + "  " + session.Tabs.ToString(CultureInfo.InvariantCulture) + " tabs  " + session.Blocks.ToString(CultureInfo.InvariantCulture) + " blocks  " +
        session.Width.ToString(CultureInfo.InvariantCulture) + "x" + session.Height.ToString(CultureInfo.InvariantCulture) + "  " +
        (session.Clients > 0 ? "attached" : "detached") + "  created " + session.CreatedAt.ToString("u", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a block as one line.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The line.</returns>
    internal static string FormatBlock(BlockInfo block) =>
        (block.Active ? "* " : "  ") + block.Id + "  " + block.Session + ":" + block.Tab + "." + block.Index.ToString(CultureInfo.InvariantCulture) + "  " +
        block.Width.ToString(CultureInfo.InvariantCulture) + "x" + block.Height.ToString(CultureInfo.InvariantCulture) + "  " +
        block.State.ToString().ToLowerInvariant() + (block.ExitCode is { } code ? " " + code.ToString(CultureInfo.InvariantCulture) : string.Empty) +
        (block.Pid is { } pid ? "  pid " + pid.ToString(CultureInfo.InvariantCulture) : string.Empty) + "  " + block.Title;

    /// <summary>
    /// Runs a command body against a connected client, starting the server when needed.
    /// </summary>
    /// <param name="parseResult">The parse result.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <param name="body">The body.</param>
    /// <returns>The exit code.</returns>
    internal static Task<int> InvokeAsync(ParseResult parseResult, Func<CommandContext, ControlClient, Task> body, CancellationToken cancellationToken) =>
        InvokeWithCodeAsync(parseResult, async (context, client) =>
        {
            await body(context, client).ConfigureAwait(false);
            return 0;
        }, cancellationToken);

    /// <summary>
    /// Runs a command body that chooses its own exit code against a connected client.
    /// </summary>
    /// <param name="parseResult">The parse result.</param>
    /// <param name="body">The body.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> InvokeWithCodeAsync(ParseResult parseResult, Func<CommandContext, ControlClient, Task<int>> body, CancellationToken cancellationToken)
    {
        var context = CommandContext.From(parseResult);
        await using (context.ConfigureAwait(false))
        {
            try
            {
                ControlClient client = await context.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return await body(context, client).ConfigureAwait(false);
            }
            catch (ProtocolException exception)
            {
                return CommandContext.Fail(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return CommandContext.Fail(exception.Message);
            }
            catch (OperationCanceledException)
            {
                return 130;
            }
        }
    }

    /// <summary>
    /// Runs a command body only when a server is running.
    /// </summary>
    /// <param name="parseResult">The parse result.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <param name="body">The body.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> InvokeIfRunningAsync(ParseResult parseResult, Func<CommandContext, ControlClient, Task> body, CancellationToken cancellationToken)
    {
        var context = CommandContext.From(parseResult);
        await using (context.ConfigureAwait(false))
        {
            try
            {
                ControlClient? client = await context.TryConnectAsync(cancellationToken).ConfigureAwait(false);
                if (client is null)
                {
                    await Console.Out.WriteLineAsync(context.Json ? "{}" : "no server running").ConfigureAwait(false);

                    return 0;
                }

                await body(context, client).ConfigureAwait(false);
                return 0;
            }
            catch (ProtocolException exception)
            {
                return CommandContext.Fail(exception.Message);
            }
            catch (OperationCanceledException)
            {
                return 130;
            }
        }
    }
}
