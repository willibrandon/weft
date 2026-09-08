using Hex1b;
using Hex1b.Tokens;

namespace Weft.Client;

/// <summary>
/// Reports that the user touched this client whenever input reaches the app, so the server can treat it as active.
/// </summary>
/// <param name="touched">Called on every input batch.</param>
internal sealed class InputActivityFilter(Action touched) : IHex1bTerminalWorkloadFilter
{
    /// <inheritdoc />
    public ValueTask OnSessionStartAsync(int width, int height, DateTimeOffset timestamp, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnOutputAsync(IReadOnlyList<AnsiToken> tokens, TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnFrameCompleteAsync(TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnInputAsync(IReadOnlyList<AnsiToken> tokens, TimeSpan elapsed, CancellationToken ct = default)
    {
        touched();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask OnResizeAsync(int width, int height, TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnSessionEndAsync(TimeSpan elapsed, CancellationToken ct = default) => ValueTask.CompletedTask;
}
