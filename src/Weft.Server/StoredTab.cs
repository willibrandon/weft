namespace Weft.Server;

/// <summary>
/// A tab as persisted for session resurrection.
/// </summary>
public sealed class StoredTab
{
    /// <summary>
    /// Gets the tab name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether the name was pinned by the user.
    /// </summary>
    public bool NamePinned { get; init; }

    /// <summary>
    /// Gets the serialized tiled layout.
    /// </summary>
    public required string Layout { get; init; }

    /// <summary>
    /// Gets the blocks in creation order.
    /// </summary>
    public required IReadOnlyList<StoredBlock> Blocks { get; init; }

    /// <summary>
    /// Gets the active block's id value, if any.
    /// </summary>
    public int? ActiveBlock { get; init; }

    /// <summary>
    /// Whether input typed into one block of the tab is fanned out to the others.
    /// </summary>
    public bool Synchronized { get; init; }
}
