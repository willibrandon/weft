using Hex1b;
using Hex1b.Tokens;

namespace Weft.Server;

/// <summary>
/// Observes a block's workload output to advance its revision and wake waiters.
/// </summary>
internal sealed class OutputRevisionFilter : IHex1bTerminalWorkloadFilter
{
    private long _revision;

    /// <summary>
    /// Gets the signal notified after each output batch.
    /// </summary>
    internal ChangeSignal Changed { get; } = new();

    /// <summary>
    /// Gets the number of output batches observed.
    /// </summary>
    internal long Revision => Volatile.Read(ref _revision);

    /// <inheritdoc />
    public ValueTask OnSessionStartAsync(int width, int height, DateTimeOffset timestamp, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnOutputAsync(IReadOnlyList<AnsiToken> tokens, TimeSpan elapsed, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _revision);
        Changed.Notify();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask OnFrameCompleteAsync(TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnInputAsync(IReadOnlyList<AnsiToken> tokens, TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnResizeAsync(int width, int height, TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnSessionEndAsync(TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;
}
