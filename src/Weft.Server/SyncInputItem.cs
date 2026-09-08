namespace Weft.Server;

/// <summary>
/// One unit of synchronized input waiting to be written to a sibling block.
/// </summary>
/// <param name="Bytes">The encoded input, ignored when <paramref name="PasteText"/> is set.</param>
/// <param name="PasteText">Text to paste, encoded according to the target's bracketed paste mode.</param>
/// <param name="Done">Completes once the target has been written, skipped, or the item was dropped.</param>
internal sealed record SyncInputItem(ReadOnlyMemory<byte> Bytes, string? PasteText, TaskCompletionSource Done);
