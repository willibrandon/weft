using Hex1b;

namespace Weft.Server;

/// <summary>
/// Wraps a block's process so input arriving through the terminal can be observed, for synchronized tabs.
/// </summary>
internal sealed class InputObservingWorkload : IHex1bTerminalWorkloadAdapter
{
    private readonly Hex1bTerminalChildProcess _process;

    /// <summary>
    /// Wraps a process.
    /// </summary>
    /// <param name="process">The process.</param>
    internal InputObservingWorkload(Hex1bTerminalChildProcess process)
    {
        _process = process;
        _process.Disconnected += () => Disconnected?.Invoke();
    }

    /// <inheritdoc />
    public event Action? Disconnected;

    /// <summary>
    /// Raised with the bytes of every input write that came through the terminal.
    /// </summary>
    internal event Action<ReadOnlyMemory<byte>>? InputWritten;

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>> ReadOutputAsync(CancellationToken ct = default) => _process.ReadOutputAsync(ct);

    /// <inheritdoc />
    public ValueTask WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        InputWritten?.Invoke(data);
        return _process.WriteInputAsync(data, ct);
    }

    /// <inheritdoc />
    public ValueTask ResizeAsync(int width, int height, CancellationToken ct = default) => _process.ResizeAsync(width, height, ct);

    /// <summary>
    /// Releases nothing; the block host owns the process.
    /// </summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
