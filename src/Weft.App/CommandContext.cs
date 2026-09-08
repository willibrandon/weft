using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Weft.Client;
using Weft.Core;
using Weft.Protocol;

namespace Weft.App;

/// <summary>
/// Per-invocation state: resolved paths, output mode, and a lazily connected control client.
/// </summary>
internal sealed class CommandContext : IAsyncDisposable
{
    private static readonly JsonSerializerOptions s_indented = new(ProtocolJsonContext.Default.Options) { WriteIndented = true };
    private ControlClient? _client;

    private CommandContext(string runtimeDirectory, bool json)
    {
        RuntimeDirectory = runtimeDirectory;
        Json = json;
    }

    /// <summary>
    /// Gets the runtime directory.
    /// </summary>
    internal string RuntimeDirectory { get; }

    /// <summary>
    /// Gets whether output is JSON.
    /// </summary>
    internal bool Json { get; }

    /// <summary>
    /// Gets the control socket path.
    /// </summary>
    internal string SocketPath => WeftPaths.ControlSocketPath(RuntimeDirectory);

    /// <summary>
    /// Builds a context from parsed options.
    /// </summary>
    /// <param name="parseResult">The parse result.</param>
    /// <returns>The context.</returns>
    internal static CommandContext From(ParseResult parseResult)
    {
        string? directory = parseResult.GetValue(CommonOptions.SocketDirectory);
        string runtime = string.IsNullOrEmpty(directory) ? WeftPaths.ResolveRuntimeDirectory() : Path.GetFullPath(directory);
        return new CommandContext(runtime, parseResult.GetValue(CommonOptions.Json));
    }

    /// <summary>
    /// Connects to the server, starting one when none is running.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>The client.</returns>
    internal async Task<ControlClient> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is { } existing)
        {
            return existing;
        }

        _client = await ServerLauncher.ConnectOrStartAsync(RuntimeDirectory, cancellationToken).ConfigureAwait(false);
        return _client;
    }

    /// <summary>
    /// Connects only when a server is already running.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>The client, or null when no server answers.</returns>
    internal async Task<ControlClient?> TryConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is { } existing)
        {
            return existing;
        }

        _client = await ServerLauncher.TryConnectAsync(SocketPath, cancellationToken).ConfigureAwait(false);
        return _client;
    }

    /// <summary>
    /// Writes a result as JSON or through a text formatter.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="value">The result.</param>
    /// <param name="typeInfo">The type information.</param>
    /// <param name="text">Formats the result as lines of text.</param>
    internal void Write<T>(T value, JsonTypeInfo<T> typeInfo, Func<T, IEnumerable<string>> text)
    {
        if (Json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(value, (JsonTypeInfo<T>)s_indented.GetTypeInfo(typeof(T))));
            return;
        }

        foreach (string line in text(value))
        {
            Console.Out.WriteLine(line);
        }

        _ = typeInfo;
    }

    /// <summary>
    /// Writes an error line and returns the failure exit code.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>Exit code 1.</returns>
    internal static int Fail(string message)
    {
        Console.Error.WriteLine("weft: " + message);
        return 1;
    }

    /// <summary>
    /// Closes the client connection.
    /// </summary>
    /// <returns>A task that completes when closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_client is { } client)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
