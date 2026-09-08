using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace Weft.Mcp;

/// <summary>
/// Builds and runs the weft MCP server over a transport.
/// </summary>
public static class WeftMcpServer
{
    /// <summary>
    /// Runs the server over standard input and output until the client disconnects.
    /// </summary>
    /// <param name="socketPath">The weft control socket path.</param>
    /// <param name="version">The server version to advertise.</param>
    /// <param name="verbose">Whether to log the SDK's request tracing as well as warnings and errors.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the client disconnects.</returns>
    public static Task RunStdioAsync(string socketPath, string version, bool verbose, CancellationToken cancellationToken) =>
        RunAsync(socketPath, version, builder => builder.WithStdioServerTransport(), verbose, cancellationToken);

    /// <summary>
    /// Runs the server over a pair of streams, for tests and embedding.
    /// </summary>
    /// <param name="socketPath">The weft control socket path.</param>
    /// <param name="version">The server version to advertise.</param>
    /// <param name="input">The stream the client writes to.</param>
    /// <param name="output">The stream the client reads from.</param>
    /// <param name="verbose">Whether to log the SDK's request tracing as well as warnings and errors.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the client disconnects.</returns>
    public static Task RunStreamsAsync(string socketPath, string version, Stream input, Stream output, bool verbose, CancellationToken cancellationToken) =>
        RunAsync(socketPath, version, builder => builder.WithStreamServerTransport(input, output), verbose, cancellationToken);

    private static async Task RunAsync(string socketPath, string version, Action<IMcpServerBuilder> transport, bool verbose, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketPath);
        var services = new ServiceCollection();
        services.AddSingleton(_ => new WeftBridge(socketPath));

        // Standard error is the channel the protocol reserves for a stdio server's logs; hosts capture it.
        services.AddSingleton<ILoggerFactory>(_ => new StderrLoggerFactory(verbose ? LogLevel.Debug : LogLevel.Warning));

        IMcpServerBuilder builder = services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "weft", Version = version };
            options.ServerInstructions = "weft hosts durable terminal sessions. A session holds tabs; a tab holds blocks; a block is one terminal. Use list_blocks to find ids, run for commands that finish on their own, and send_keys with wait_for and capture for interactive programs.";
        });
        transport(builder);

        // Tool arguments are bound with the SDK's generated context plus ours, so string arrays bind under Native AOT.
        var serializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        serializerOptions.TypeInfoResolverChain.Add(WeftMcpJsonContext.Default);
        builder.WithTools<WeftTools>(serializerOptions).WithResources<WeftResources>();
        ServiceProvider provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            McpServer server = provider.GetRequiredService<McpServer>();
            await using (server.ConfigureAwait(false))
            {
                await server.RunAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
