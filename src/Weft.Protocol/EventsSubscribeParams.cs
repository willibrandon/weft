namespace Weft.Protocol;

/// <summary>
/// Parameters for events.subscribe.
/// </summary>
public sealed class EventsSubscribeParams
{
    /// <summary>
    /// Gets the sequence number after which to replay; null starts from now.
    /// </summary>
    public long? Since { get; init; }
}
