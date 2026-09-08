using System.Text.Json.Serialization;
using Weft.Core;

namespace Weft.Protocol;

/// <summary>
/// A block as reported by the server.
/// </summary>
public sealed class BlockInfo
{
    /// <summary>
    /// Gets the block id in text form.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the one-based index within the tab in layout order.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Gets the owning tab id.
    /// </summary>
    public required string Tab { get; init; }

    /// <summary>
    /// Gets the owning session id.
    /// </summary>
    public required string Session { get; init; }

    /// <summary>
    /// The name of the session that owns the block.
    /// </summary>
    public required string SessionName { get; init; }

    /// <summary>
    /// Gets the title, from the terminal or pinned by the user.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the command file name.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Gets the command arguments.
    /// </summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>
    /// Gets the working directory the command started in.
    /// </summary>
    public required string Cwd { get; init; }

    /// <summary>
    /// Gets the process state.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<BlockState>))]
    public required BlockState State { get; init; }

    /// <summary>
    /// Gets the exit code once the process has exited.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the process id while running.
    /// </summary>
    public int? Pid { get; init; }

    /// <summary>
    /// Gets the terminal width in columns.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the terminal height in rows.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets whether the block floats above the tiled layout.
    /// </summary>
    public required bool Floating { get; init; }

    /// <summary>
    /// Gets whether the block is the active block of its tab.
    /// </summary>
    public required bool Active { get; init; }

    /// <summary>
    /// Gets the output revision, which increases on every output batch.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the path of the block's HMP1 socket.
    /// </summary>
    public required string SocketPath { get; init; }
}
