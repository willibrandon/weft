using System.Text.Json.Serialization;
using Weft.Core;

namespace Weft.Protocol;

/// <summary>
/// A session as reported by the server.
/// </summary>
public sealed record SessionInfo
{
    /// <summary>
    /// Gets the session id in text form.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the session name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets when the session was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the session's working directory for new blocks.
    /// </summary>
    public required string Cwd { get; init; }

    /// <summary>
    /// Gets the number of tabs.
    /// </summary>
    public required int Tabs { get; init; }

    /// <summary>
    /// Gets the number of blocks across tabs.
    /// </summary>
    public required int Blocks { get; init; }

    /// <summary>
    /// Gets the number of attached clients.
    /// </summary>
    public required int Clients { get; init; }

    /// <summary>
    /// Gets the authoritative width in columns.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the authoritative height in rows.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the size policy.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<SizePolicy>))]
    public required SizePolicy SizePolicy { get; init; }

    /// <summary>
    /// Gets the active tab id, if any.
    /// </summary>
    public string? ActiveTab { get; init; }
}
