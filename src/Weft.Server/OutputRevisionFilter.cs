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
    public ValueTask OnSessionStartAsync(int width, int height, DateTimeOffset timestamp, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AnsiToken>> OnOutputAsync(IReadOnlyList<AppliedToken> appliedTokens, TimeSpan elapsed, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _revision);
        Changed.Notify();
        Output?.Invoke();
        var tokens = new AnsiToken[appliedTokens.Count];
        for (int i = 0; i < tokens.Length; i++)
        {
            tokens[i] = appliedTokens[i].Token;
        }

        return ValueTask.FromResult<IReadOnlyList<AnsiToken>>(tokens);
    }

    /// <inheritdoc />
    public ValueTask OnInputAsync(IReadOnlyList<AnsiToken> tokens, TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnResizeAsync(int width, int height, TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnSessionEndAsync(TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;
}
