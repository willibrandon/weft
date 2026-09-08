using System.Text.Json.Serialization;
using Weft.Core;

namespace Weft.Protocol;

/// <summary>
/// Parameters for layout.preset.
/// </summary>
public sealed class LayoutPresetParams
{
    /// <summary>
    /// Gets the target tab.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the preset; null cycles to the next preset.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<LayoutPreset>))]
    public LayoutPreset? Preset { get; init; }

    /// <summary>
    /// Gets the main block's share for the main presets.
    /// </summary>
    public int MainPercent { get; set; } = 50;
}
