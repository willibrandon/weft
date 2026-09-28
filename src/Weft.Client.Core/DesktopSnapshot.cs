using Hex1b.Automation;

namespace Weft.Client;

/// <summary>
/// Keeps a completed presentation alive while a frame reader projects its cells and graphics.
/// </summary>
internal sealed class DesktopSnapshot : IDisposable
{
    private int _references = 1;

    /// <summary>
    /// Takes ownership of one immutable snapshot.
    /// </summary>
    /// <param name="value">The snapshot to release after its last reader.</param>
    internal DesktopSnapshot(Hex1bTerminalSnapshot value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the retained presentation.
    /// </summary>
    internal Hex1bTerminalSnapshot Value { get; }

    /// <summary>
    /// Adds a reader while the caller still owns an existing reference.
    /// </summary>
    /// <returns>The same snapshot with another release obligation.</returns>
    internal DesktopSnapshot Retain()
    {
        _ = Interlocked.Increment(ref _references);
        return this;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Decrement(ref _references) == 0)
        {
            Value.Dispose();
        }
    }
}
