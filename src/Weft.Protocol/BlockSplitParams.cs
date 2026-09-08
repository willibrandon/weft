using System.Text.Json.Serialization;
using Weft.Core;

namespace Weft.Protocol;

/// <summary>
/// Parameters for block.split.
/// </summary>
public sealed class BlockSplitParams
{
    /// <summary>
    /// Gets the block to split.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the split orientation.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<SplitOrientation>))]
    public required SplitOrientation Orientation { get; init; }

    /// <summary>
    /// Gets the size of the new block along the orientation; null gives it half.
    /// </summary>
    public int? Size { get; init; }

    /// <summary>
    /// Gets whether the new block goes before the target instead of after.
    /// </summary>
    public bool Before { get; init; }

    /// <summary>
    /// Gets the command; null runs the default shell.
    /// </summary>
    public IReadOnlyList<string>? Command { get; init; }

    /// <summary>
    /// Gets the working directory; null inherits the target block's start directory.
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Gets whether the new block becomes active.
    /// </summary>
    public bool Focus { get; init; } = true;
}
