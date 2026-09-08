namespace Weft.Protocol;

/// <summary>
/// Payload of subscriber.paused.
/// </summary>
public sealed class SubscriberPausedData
{
    /// <summary>
    /// Gets how many events were dropped for this subscriber.
    /// </summary>
    public required long Dropped { get; init; }
}
