namespace Weft.Server;

/// <summary>
/// A block resize computed under the registry lock and applied outside it.
/// </summary>
/// <param name="Host">The block host.</param>
/// <param name="Width">The new width in columns.</param>
/// <param name="Height">The new height in rows.</param>
internal readonly record struct PendingResize(BlockHost Host, int Width, int Height);
