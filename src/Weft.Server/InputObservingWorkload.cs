using Hex1b;

namespace Weft.Server;

/// <summary>
/// Observes synchronized input and bounds concurrent terminal output processing.
/// </summary>
internal sealed class InputObservingWorkload : IHex1bTerminalWorkloadAdapter
{
    private const int LineBreaksPerBatch = 8;
    private readonly SemaphoreSlim _outputProcessing;
    private readonly Hex1bTerminalChildProcess _process;
    private readonly CursorControlReader _cursorControls = new();
    private ReadOnlyMemory<byte> _pendingOutput;
    private CancellationTokenRegistration _processingCancellation;
    private int _pendingBytes;
    private int _applying;
    private int _processing;

    /// <summary>
    /// Wraps a process.
    /// </summary>
    /// <param name="process">The process.</param>
    /// <param name="outputProcessing">The server-owned output processing slot.</param>
    internal InputObservingWorkload(Hex1bTerminalChildProcess process, SemaphoreSlim outputProcessing)
    {
        _process = process;
        _outputProcessing = outputProcessing;
        _process.Disconnected += () => Disconnected?.Invoke();
    }

    /// <inheritdoc />
    public event Action? Disconnected;

    /// <summary>
    /// Raised with the bytes of every input write that came through the terminal.
    /// </summary>
    internal event Action<ReadOnlyMemory<byte>>? InputWritten;

    /// <summary>
    /// Gets or sets the callback invoked after a returned batch has been applied, before reading another.
    /// </summary>
    internal Func<CursorControlReader, CancellationToken, ValueTask>? OutputApplied { get; set; }

    /// <summary>
    /// Gets whether output has been read but not yet applied to the terminal.
    /// </summary>
    internal bool HasPendingOutput => Volatile.Read(ref _pendingBytes) != 0 || Volatile.Read(ref _applying) != 0;

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> ReadOutputAsync(CancellationToken ct = default)
    {
        await _processingCancellation.DisposeAsync().ConfigureAwait(false);
        if (Volatile.Read(ref _applying) != 0 && OutputApplied is { } applied)
        {
            await applied(_cursorControls, ct).ConfigureAwait(false);
        }
        CompleteOutput();
        ct.ThrowIfCancellationRequested();
        if (_pendingOutput.IsEmpty)
        {
            _pendingOutput = await _process.ReadOutputAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _pendingBytes, _pendingOutput.Length);
        }
        if (_pendingOutput.IsEmpty)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        // Each scroll touches the screen. Small batches keep other
        // terminals responsive without fragmenting ordinary graphics payloads.
        int length = _pendingOutput.Length;
        int lines = 0;
        for (int index = 0; index < _pendingOutput.Length; index++)
        {
            if (_pendingOutput.Span[index] == (byte)'\n' && ++lines == LineBreaksPerBatch)
            {
                length = index + 1;
                break;
            }
        }

        length = _cursorControls.Read(_pendingOutput.Span[..length]);

        // Ordinary character echo and graphics do not need to wait behind
        // scrolling terminals. The slot bounds newline-driven screen changes.
        if (_pendingOutput.Span[..length].Contains((byte)'\n'))
        {
            await _outputProcessing.WaitAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _processing, 1);
            _processingCancellation = ct.UnsafeRegister(static state => ((InputObservingWorkload)state!).CompleteOutput(), this);
        }
        Volatile.Write(ref _applying, 1);
        ReadOnlyMemory<byte> batch = _pendingOutput[..length];
        _pendingOutput = _pendingOutput[length..];
        Volatile.Write(ref _pendingBytes, _pendingOutput.Length);
        return batch;
    }

    /// <summary>
    /// Releases the processing slot once a terminal has applied its output.
    /// </summary>
    internal void CompleteOutput()
    {
        Volatile.Write(ref _applying, 0);
        if (Interlocked.Exchange(ref _processing, 0) != 0)
        {
            _ = _outputProcessing.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        InputWritten?.Invoke(data);
        return _process.WriteInputAsync(data, ct);
    }

    /// <inheritdoc />
    public ValueTask ResizeAsync(int width, int height, CancellationToken ct = default)
    {
        return _process.ResizeAsync(width, height, ct);
    }

    /// <summary>
    /// Releases pending output and its processing slot; the block host owns the process.
    /// </summary>
    /// <returns>A completed task.</returns>
    public async ValueTask DisposeAsync()
    {
        await _processingCancellation.DisposeAsync().ConfigureAwait(false);
        CompleteOutput();
        _pendingOutput = ReadOnlyMemory<byte>.Empty;
        Volatile.Write(ref _pendingBytes, 0);
    }
}
