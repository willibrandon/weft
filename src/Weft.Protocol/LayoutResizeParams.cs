using System.Text.Json.Serialization;
using Weft.Core;

namespace Weft.Protocol;

/// <summary>
/// Parameters for layout.resize.
/// </summary>
public sealed class LayoutResizeParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the edge to move.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<LayoutDirection>))]
    public required LayoutDirection Direction { get; init; }

    /// <summary>
    /// Gets the number of cells.
    /// </summary>
    public int Amount { get; set; } = 1;
}
