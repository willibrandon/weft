namespace Weft.Server;

/// <summary>
/// One unit of synchronized input waiting to be written to sibling blocks.
/// </summary>
/// <param name="Targets">The hosts to write to, in tab order.</param>
/// <param name="Bytes">The encoded input, ignored when <paramref name="PasteText"/> is set.</param>
/// <param name="PasteText">Text to paste, encoded per target according to its bracketed paste mode.</param>
/// <param name="Done">Completes once every target has been written or skipped.</param>
internal sealed record SyncInputItem(IReadOnlyList<BlockHost> Targets, ReadOnlyMemory<byte> Bytes, string? PasteText, TaskCompletionSource Done);
