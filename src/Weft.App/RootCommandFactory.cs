using System.CommandLine;

namespace Weft.App;

/// <summary>
/// Composes the weft command tree.
/// </summary>
internal static class RootCommandFactory
{
    /// <summary>
    /// Creates the root command with every subcommand.
    /// </summary>
    /// <returns>The root command.</returns>
    internal static RootCommand Create()
    {
        var root = new RootCommand("weft: durable terminal sessions and a multiplexer for all work.");
        root.Options.Add(CommonOptions.Json);
        root.Options.Add(CommonOptions.SocketDirectory);
        Argument<string?> target = CommonOptions.OptionalTarget("Session to attach to; created when missing.");
        root.Arguments.Add(target);
        root.Options.Add(AttachCommand.ReadOnly);
        root.SetAction((parseResult, cancellationToken) => AttachCommand.RunAsync(parseResult, parseResult.GetValue(target), cancellationToken));
        root.Subcommands.Add(AttachCommand.Create());
        root.Subcommands.Add(SessionCommands.CreateNew());
        root.Subcommands.Add(SessionCommands.CreateList());
        root.Subcommands.Add(SessionCommands.CreateKillSession());
        root.Subcommands.Add(SessionCommands.CreateInfo());
        root.Subcommands.Add(SessionCommands.CreateTabs());
        root.Subcommands.Add(SessionCommands.CreateBlocks());
        root.Subcommands.Add(SessionCommands.CreateSync());
        root.Subcommands.Add(BlockCommands.CreateSplit());
        root.Subcommands.Add(BlockCommands.CreateSend());
        root.Subcommands.Add(BlockCommands.CreateType());
        root.Subcommands.Add(BlockCommands.CreatePaste());
        root.Subcommands.Add(BlockCommands.CreateCapture());
        root.Subcommands.Add(BlockCommands.CreateWait());
        root.Subcommands.Add(BlockCommands.CreateRun());
        root.Subcommands.Add(BlockCommands.CreateFocus());
        root.Subcommands.Add(BlockCommands.CreateZoom());
        root.Subcommands.Add(BlockCommands.CreateClose());
        root.Subcommands.Add(BlockCommands.CreateKill());
        root.Subcommands.Add(BlockCommands.CreateRename());
        root.Subcommands.Add(BlockCommands.CreateFloat());
        root.Subcommands.Add(BlockCommands.CreateTile());
        root.Subcommands.Add(BlockCommands.CreateMove());
        root.Subcommands.Add(LayoutCommands.CreateLayout());
        root.Subcommands.Add(LayoutCommands.CreateResize());
        root.Subcommands.Add(EventCommands.CreateEvents());
        root.Subcommands.Add(EventCommands.CreateSignal());
        root.Subcommands.Add(EventCommands.CreateWaitFor());
        root.Subcommands.Add(ServerCommand.Create());
        root.Subcommands.Add(McpCommand.Create());
        root.Subcommands.Add(ServerCommand.CreateShutdown());
        return root;
    }
}
