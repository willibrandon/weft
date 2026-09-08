using System.Text.Json.Serialization;
using Weft.Core;

namespace Weft.Protocol;

/// <summary>
/// Parameters for block.focus.
/// </summary>
public sealed class BlockFocusParams
{
    /// <summary>
    /// Gets the block to focus, or the block to move from when a direction is given.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the direction to move focus in from the target.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<LayoutDirection>))]
    public LayoutDirection? Direction { get; init; }
}
