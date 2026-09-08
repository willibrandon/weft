namespace Weft.Protocol;

/// <summary>
/// Parameters for wait.for and wait.signal.
/// </summary>
public sealed class WaitChannelParams
{
    /// <summary>
    /// Gets the channel name.
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// Gets the timeout in milliseconds for wait.for.
    /// </summary>
    public int TimeoutMs { get; init; } = 30_000;
}
