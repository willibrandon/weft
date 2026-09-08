namespace Weft.Server;

/// <summary>
/// A block as persisted for session resurrection.
/// </summary>
public sealed class StoredBlock
{
    /// <summary>
    /// Gets the block id value at save time, used to map layout leaves.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the command file name.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Gets the command arguments.
    /// </summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>
    /// Gets the working directory.
    /// </summary>
    public required string Cwd { get; init; }

    /// <summary>
    /// Gets the pinned title, if any.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets whether the block floats.
    /// </summary>
    public bool Floating { get; init; }

    /// <summary>
    /// Gets the floating column.
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Gets the floating row.
    /// </summary>
    public int Y { get; init; }

    /// <summary>
    /// Gets the floating width.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Gets the floating height.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Whether the block stays out of its tab's synchronized input.
    /// </summary>
    public bool ExcludedFromSync { get; init; }
}
