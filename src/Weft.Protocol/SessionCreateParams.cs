using System.Text.Json.Serialization;
using Weft.Core;

namespace Weft.Protocol;

/// <summary>
/// Parameters for session.create.
/// </summary>
public sealed class SessionCreateParams
{
    /// <summary>
    /// Gets the session name; null picks the next free numbered name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the working directory for the first block; null uses the server's home directory.
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Gets the command for the first block; null runs the default shell.
    /// </summary>
    public IReadOnlyList<string>? Command { get; init; }

    /// <summary>
    /// Gets the initial width in columns.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Gets the initial height in rows.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Gets the size policy; null uses the server default.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<SizePolicy>))]
    public SizePolicy? SizePolicy { get; init; }
}
