using System.CommandLine;

namespace Weft.App;

/// <summary>
/// Entry point for the weft executable.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Parses the command line and runs the selected command.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The process exit code.</returns>
    private static async Task<int> Main(string[] args)
    {
        RootCommand root = new("weft: durable terminal sessions and a multiplexer for all work.");
        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }
}
