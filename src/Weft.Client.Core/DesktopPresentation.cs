using Hex1b;
using Hex1b.Automation;
using Hex1b.Tokens;
using System.Globalization;

namespace Weft.Client;

/// <summary>
/// Gives the desktop renderer graphics capabilities while retaining Hex1b input and cell-impact handling.
/// </summary>
internal sealed class DesktopPresentation : ITerminalLifecycleAwarePresentationAdapter, ICellImpactAwarePresentationAdapter
{
    private readonly TerminalWidgetHandle _handle;
    private readonly Action _invalidate;
    private readonly Timer _synchronizedTimer;
    private int _cursorShape;
    private readonly Lock _snapshotGate = new();
    private Hex1bTerminal? _terminal;
    private DesktopSnapshot? _snapshot;
    private long _captureSequence;
    private long _publishedSequence;
    private long _synchronizedUntil;
    private bool _disposed;

    /// <summary>
    /// Takes ownership of a terminal handle used by the native view.
    /// </summary>
    /// <param name="handle">The handle to wrap.</param>
    /// <param name="invalidate">Schedules a native frame for presentation-only changes.</param>
    internal DesktopPresentation(TerminalWidgetHandle handle, Action invalidate)
    {
        _handle = handle;
        _invalidate = invalidate;
        _synchronizedTimer = new Timer(_ => _invalidate(), null, Timeout.Infinite, Timeout.Infinite);
        Capabilities = handle.Capabilities with { SupportsKgp = true, SupportsSixel = true };
    }

    /// <summary>
    /// Gets the cursor style requested by the output stream, including complete DECSCUSR sequences.
    /// </summary>
    internal int CursorShape => Volatile.Read(ref _cursorShape);

    /// <summary>
    /// Captures the initial empty screen after terminal construction has completed.
    /// </summary>
    internal void Initialize()
    {
        CommitSnapshot();
    }

    /// <summary>
    /// Reads the last completed presentation while retaining its graphics and hyperlink owners.
    /// </summary>
    /// <typeparam name="T">The projected frame type.</typeparam>
    /// <param name="read">Projects the immutable snapshot without retaining it.</param>
    /// <returns>The projection.</returns>
    internal T ReadSnapshot<T>(Func<Hex1bTerminalSnapshot, T> read)
    {
        long deadline = Volatile.Read(ref _synchronizedUntil);
        if (deadline != 0 && deadline <= Environment.TickCount64)
        {
            CommitSnapshot();
        }
        using DesktopSnapshot snapshot = RetainSnapshot();
        return read(snapshot.Value);
    }

    private DesktopSnapshot RetainSnapshot()
    {
        lock (_snapshotGate)
        {
            return (_snapshot ?? throw new InvalidOperationException("The terminal presentation is unavailable.")).Retain();
        }
    }

    /// <inheritdoc />
    public int Width => _handle.Width;

    /// <inheritdoc />
    public int Height => _handle.Height;

    /// <inheritdoc />
    public TerminalCapabilities Capabilities { get; }

    /// <inheritdoc />
    public event Action<int, int>? Resized
    {
        add => _handle.Resized += value;
        remove => _handle.Resized -= value;
    }

    /// <inheritdoc />
    public event Action? Disconnected
    {
        add => _handle.Disconnected += value;
        remove => _handle.Disconnected -= value;
    }

    /// <inheritdoc />
    public ValueTask WriteOutputAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        return _handle.WriteOutputAsync(data, ct);
    }

    /// <inheritdoc />
    public ValueTask WriteOutputWithImpactsAsync(IReadOnlyList<AppliedToken> appliedTokens, CancellationToken ct = default)
    {
        foreach (AnsiToken token in appliedTokens.Select(value => value.Token))
        {
            int? shape = token switch
            {
                CursorShapeToken cursor => cursor.Shape,
                UnrecognizedSequenceToken sequence => ReadCursorStyle(sequence.Sequence),
                RisToken => 0,
                _ => null
            };
            if (shape is { } value)
            {
                Volatile.Write(ref _cursorShape, value);
            }
            if (token is PrivateModeToken { Mode: 2026 } synchronized)
            {
                lock (_snapshotGate)
                {
                    if (!_disposed)
                    {
                        if (!synchronized.Enable)
                        {
                            Volatile.Write(ref _synchronizedUntil, 0);
                            _ = _synchronizedTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        }
                        else if (Volatile.Read(ref _synchronizedUntil) == 0)
                        {
                            Volatile.Write(ref _synchronizedUntil, Environment.TickCount64 + 1000);
                            _ = _synchronizedTimer.Change(1000, Timeout.Infinite);
                        }
                    }
                }
            }
        }
        ValueTask output = _handle.WriteOutputWithImpactsAsync(appliedTokens, ct);
        CommitSnapshot();
        return output;
    }

    private static int? ReadCursorStyle(string sequence)
    {
        int prefix = sequence.StartsWith("\x1b[", StringComparison.Ordinal) ? 2 : sequence.StartsWith('\u009b') ? 1 : 0;
        if (prefix == 0 || sequence.Length < prefix + 2 || !sequence.EndsWith(" q", StringComparison.Ordinal))
        {
            return null;
        }

        ReadOnlySpan<char> parameter = sequence.AsSpan(prefix, sequence.Length - prefix - 2);
        return parameter.IsEmpty ? 0
            : int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out int shape) && shape is >= 0 and <= 6 ? shape : null;
    }

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>> ReadInputAsync(CancellationToken ct = default)
    {
        return _handle.ReadInputAsync(ct);
    }

    /// <inheritdoc />
    public void InvalidatePresentation()
    {
        ((IHex1bTerminalPresentationAdapter)_handle).InvalidatePresentation();
        CommitSnapshot();
    }

    /// <inheritdoc />
    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        return _handle.FlushAsync(ct);
    }

    /// <inheritdoc />
    public ValueTask EnterRawModeAsync(CancellationToken ct = default)
    {
        return _handle.EnterRawModeAsync(ct);
    }

    /// <inheritdoc />
    public ValueTask ExitRawModeAsync(CancellationToken ct = default)
    {
        return _handle.ExitRawModeAsync(ct);
    }

    /// <inheritdoc />
    public (int Row, int Column) GetCursorPosition()
    {
        return _handle.GetCursorPosition();
    }

    /// <inheritdoc />
    public void TerminalCreated(Hex1bTerminal terminal)
    {
        ((ITerminalLifecycleAwarePresentationAdapter)_handle).TerminalCreated(terminal);
        _terminal = terminal;
    }

    /// <inheritdoc />
    public void TerminalStarted()
    {
        ((ITerminalLifecycleAwarePresentationAdapter)_handle).TerminalStarted();
    }

    /// <inheritdoc />
    public void TerminalCompleted(int exitCode)
    {
        ((ITerminalLifecycleAwarePresentationAdapter)_handle).TerminalCompleted(exitCode);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_snapshotGate)
        {
            _disposed = true;
            _synchronizedTimer.Dispose();
            _snapshot?.Dispose();
            _snapshot = null;
        }
        return _handle.DisposeAsync();
    }

    private void CommitSnapshot()
    {
        if (_terminal is null || Volatile.Read(ref _synchronizedUntil) > Environment.TickCount64)
        {
            return;
        }
        Volatile.Write(ref _synchronizedUntil, 0);
        long sequence = Interlocked.Increment(ref _captureSequence);
        var next = new DesktopSnapshot(_terminal.CreateSnapshot());
        DesktopSnapshot? previous;
        lock (_snapshotGate)
        {
            if (_disposed || sequence <= _publishedSequence)
            {
                next.Dispose();
                return;
            }
            _publishedSequence = sequence;
            previous = _snapshot;
            _snapshot = next;
        }
        previous?.Dispose();
        _invalidate();
    }
}
