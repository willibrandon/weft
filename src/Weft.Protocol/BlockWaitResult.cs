using System.Text.Json.Serialization;

namespace Weft.Protocol;

/// <summary>
/// Result of block.wait.
/// </summary>
public sealed class BlockWaitResult
{
    /// <summary>
    /// Gets why the wait returned.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<WaitOutcome>))]
    public required WaitOutcome Outcome { get; init; }

    /// <summary>
    /// Gets the block's revision when the wait returned.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the exit code when the process has exited.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the matched text when a pattern matched.
    /// </summary>
    public string? Match { get; init; }

    /// <summary>
    /// Gets the full line that matched.
    /// </summary>
    public string? Line { get; init; }
}
