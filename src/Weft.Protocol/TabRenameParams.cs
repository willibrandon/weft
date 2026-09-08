namespace Weft.Protocol;

/// <summary>
/// Parameters for tab.rename.
/// </summary>
public sealed class TabRenameParams
{
    /// <summary>
    /// Gets the target tab.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the new name.
    /// </summary>
    public required string Name { get; init; }
}
