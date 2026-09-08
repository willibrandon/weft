using System.Text.Json.Serialization;

namespace Weft.Protocol;

/// <summary>
/// Parameters for block.capture.
/// </summary>
public sealed class BlockCaptureParams
{
    /// <summary>
    /// Gets the target block.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets how many scrollback lines to include above the screen.
    /// </summary>
    public int History { get; init; }

    /// <summary>
    /// Gets the output format.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<CaptureFormat>))]
    public CaptureFormat Format { get; init; }
}
