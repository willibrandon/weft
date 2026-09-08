using System.Text.Json.Serialization;

namespace Weft.Core;

/// <summary>
/// The user configuration file, read by both the server and the client.
/// </summary>
public sealed class WeftConfig
{
    /// <summary>
    /// Gets the leader chord text, such as <c>ctrl+b</c>.
    /// </summary>
    public string Leader { get; init; } = "ctrl+b";

    /// <summary>
    /// Gets whether blocks draw a one-cell frame with a title.
    /// </summary>
    public bool Frames { get; init; } = true;

    /// <summary>
    /// Gets the size policy for new sessions.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SizePolicy>))]
    public SizePolicy SizePolicy { get; init; } = SizePolicy.Latest;

    /// <summary>
    /// Gets the scrollback rows kept per block.
    /// </summary>
    public int Scrollback { get; init; } = 10_000;

    /// <summary>
    /// Gets the shell for new blocks, or null for the environment's shell.
    /// </summary>
    public string? Shell { get; init; }

    /// <summary>
    /// Gets the theme name: default, ocean, high-contrast, or sunset.
    /// </summary>
    public string Theme { get; init; } = "default";

    /// <summary>
    /// Gets the width for sessions with no client.
    /// </summary>
    public int DefaultWidth { get; init; } = 120;

    /// <summary>
    /// Gets the height for sessions with no client.
    /// </summary>
    public int DefaultHeight { get; init; } = 36;

    /// <summary>
    /// Gets key binding overrides: chord text to action id, or <c>none</c> to unbind.
    /// </summary>
    public Dictionary<string, string> Bindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets hooks: event name to a shell command run with the event in environment variables.
    /// </summary>
    public Dictionary<string, string> Hooks { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
