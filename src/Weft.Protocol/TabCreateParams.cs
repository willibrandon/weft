namespace Weft.Protocol;

/// <summary>
/// Parameters for tab.create.
/// </summary>
public sealed class TabCreateParams
{
    /// <summary>
    /// Gets the target session.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the tab name; null uses the first block's title.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the working directory for the first block.
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Gets the command for the first block; null runs the default shell.
    /// </summary>
    public IReadOnlyList<string>? Command { get; init; }
}
