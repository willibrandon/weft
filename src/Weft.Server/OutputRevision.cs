namespace Weft.Server;

/// <summary>
/// Signals output only after its batch has been applied to the authoritative terminal.
/// </summary>
internal sealed class OutputRevision
{
    private long _revision;

    /// <summary>
    /// Gets the signal raised after each applied output chunk.
    /// </summary>
    internal ChangeSignal Changed { get; } = new();

    /// <summary>
    /// Gets the number of output chunks applied so far.
    /// </summary>
    internal long Revision => Volatile.Read(ref _revision);

    /// <summary>
    /// Publishes the completion of one output batch.
    /// </summary>
    internal void Advance()
    {
        _ = Interlocked.Increment(ref _revision);
        Changed.Notify();
    }
}
