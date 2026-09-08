using System.Text.Json.Serialization;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// A session as persisted for resurrection after a server restart.
/// </summary>
public sealed class StoredSession
{
    /// <summary>
    /// Gets the session name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the working directory for new blocks.
    /// </summary>
    public required string Cwd { get; init; }

    /// <summary>
    /// Gets the authoritative width.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the authoritative height.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the size policy.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<SizePolicy>))]
    public required SizePolicy SizePolicy { get; init; }

    /// <summary>
    /// Gets the tabs in index order.
    /// </summary>
    public required IReadOnlyList<StoredTab> Tabs { get; init; }

    /// <summary>
    /// Gets the active tab's zero-based index.
    /// </summary>
    public int ActiveTab { get; init; }

    /// <summary>
    /// Gets when the session was last saved.
    /// </summary>
    public required DateTimeOffset SavedAt { get; init; }
}
