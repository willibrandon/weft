using Hex1b;
using Hex1b.Tokens;

namespace Weft.Server;

/// <summary>
/// Advances a revision once output has been applied to the screen, so waits and captures see it.
/// </summary>
/// <remarks>
/// This is a presentation filter on purpose. Workload filters see bytes before the terminal has
/// applied them, so a wait that woke on that signal could capture a stale screen and never be
/// woken again for the same output. Applied tokens arrive after the buffer reflects them.
/// </remarks>
internal sealed class OutputRevisionFilter : IHex1bTerminalPresentationFilter
{
    private readonly CursorReplay _cursorReplay = new();
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
    /// Raised after each applied output chunk.
    /// </summary>
    internal event Action? Output;

    /// <inheritdoc />
    public ValueTask OnSessionStartAsync(int width, int height, DateTimeOffset timestamp, CancellationToken ct = default)
    {
        _cursorReplay.Resize(width);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AnsiToken>> OnOutputAsync(IReadOnlyList<AppliedToken> appliedTokens, TimeSpan elapsed, CancellationToken ct = default)
    {
        _ = Interlocked.Increment(ref _revision);
        Changed.Notify();
        Output?.Invoke();
        return ValueTask.FromResult(_cursorReplay.Project(appliedTokens));
    }

    /// <inheritdoc />
    public ValueTask OnInputAsync(IReadOnlyList<AnsiToken> tokens, TimeSpan elapsed, CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask OnResizeAsync(int width, int height, TimeSpan elapsed, CancellationToken ct = default)
    {
        _cursorReplay.Resize(width);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask OnSessionEndAsync(TimeSpan elapsed, CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }
}
