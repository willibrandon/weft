using System.CommandLine;
using Weft.Mcp;

namespace Weft.App;

/// <summary>
/// The mcp command: a Model Context Protocol server over standard input and output.
/// </summary>
internal static class McpCommand
{
    /// <summary>
    /// Creates the mcp command.
    /// </summary>
    /// <returns>The command.</returns>
    internal static Command Create()
    {
        var command = new Command("mcp", "Serve weft to an agent over the Model Context Protocol on standard input and output.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = CommandContext.From(parseResult);
            await using (context.ConfigureAwait(false))
            {
                try
                {
                    await context.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    return CommandContext.Fail(exception.Message);
                }

                Console.SetOut(TextWriter.Null);
                await WeftMcpServer.RunStdioAsync(context.SocketPath, ServerCommand.Version, cancellationToken).ConfigureAwait(false);
                return 0;
            }
        });
        return command;
    }
}
