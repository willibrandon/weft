using System.Text.Json.Serialization;

namespace Weft.Core;

/// <summary>
/// The user configuration file, read by both the server and the client.
/// </summary>
/// <remarks>
/// Every member is settable rather than init-only on purpose. The .NET 10 System.Text.Json source
/// generator treats init-only members as constructor parameters, so a type with any of them is
/// created through that path and a value the file omits becomes null or zero instead of the
/// initializer. The dictionaries are read-only and populated in place.
/// </remarks>
public sealed class WeftConfig
{
    /// <summary>
    /// Gets the leader chord text, such as <c>ctrl+b</c>.
    /// </summary>
    public string Leader { get; set; } = "ctrl+b";

    /// <summary>
    /// Gets whether blocks draw a one-cell frame with a title.
    /// </summary>
    public bool Frames { get; set; } = true;

    /// <summary>
    /// Gets the size policy for new sessions.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SizePolicy>))]
    public SizePolicy SizePolicy { get; set; } = SizePolicy.Latest;

    /// <summary>
    /// Gets the scrollback rows kept per block.
    /// </summary>
    public int Scrollback { get; set; } = 10_000;

    /// <summary>
    /// Gets the shell for new blocks, or null for the environment's shell.
    /// </summary>
    public string? Shell { get; set; }

    /// <summary>
    /// Gets the theme name: default, ocean, high-contrast, or sunset.
    /// </summary>
    public string Theme { get; set; } = "default";

    /// <summary>
    /// Gets the width for sessions with no client.
    /// </summary>
    public int DefaultWidth { get; set; } = 120;

    /// <summary>
    /// Gets the height for sessions with no client.
    /// </summary>
    public int DefaultHeight { get; set; } = 36;

    /// <summary>
    /// Gets key binding overrides: chord text to action id, or <c>none</c> to unbind.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> Bindings { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets hooks: event name to a shell command run with the event in environment variables.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> Hooks { get; } = new(StringComparer.OrdinalIgnoreCase);
}
