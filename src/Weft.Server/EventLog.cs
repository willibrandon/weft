using System.Text.Json.Serialization.Metadata;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Assigns sequence numbers to events, keeps a replay ring, and fans events out to subscribers.
/// </summary>
internal sealed class EventLog
{
    private const int RingCapacity = 4096;
    private readonly Lock _gate = new();
    private readonly ProtocolMessage?[] _ring = new ProtocolMessage?[RingCapacity];
    private readonly List<EventSubscription> _subscribers = [];
    private long _seq;

    /// <summary>
    /// Gets the latest sequence number.
    /// </summary>
    internal long Seq
    {
        get
        {
            lock (_gate)
            {
                return _seq;
            }
        }
    }

    /// <summary>
    /// Publishes an event to every subscriber.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="name">The event name.</param>
    /// <param name="data">The payload.</param>
    /// <param name="typeInfo">The payload type information.</param>
    /// <returns>The sequence number assigned.</returns>
    internal long Publish<T>(string name, T data, JsonTypeInfo<T> typeInfo)
    {
        EventSubscription[] subscribers;
        ProtocolMessage message;
        lock (_gate)
        {
            _seq++;
            message = ProtocolCodec.Event(name, _seq, data, typeInfo);
            _ring[_seq % RingCapacity] = message;
            subscribers = [.. _subscribers];
        }

        foreach (EventSubscription subscriber in subscribers)
        {
            subscriber.Offer(message);
        }

        return message.Seq ?? 0;
    }

    /// <summary>
    /// Subscribes to events, replaying those after a sequence number when they are still in the ring.
    /// </summary>
    /// <param name="since">The last sequence number the subscriber has seen, or null for none.</param>
    /// <returns>The subscription.</returns>
    internal EventSubscription Subscribe(long? since)
    {
        lock (_gate)
        {
            var subscription = new EventSubscription(Unsubscribe);
            try
            {
                if (since is { } last)
                {
                    Replay(subscription, last);
                }

                _subscribers.Add(subscription);
            }
            catch
            {
                subscription.Dispose();
                throw;
            }

            return subscription;
        }
    }

    private void Replay(EventSubscription subscription, long last)
    {
        long oldest = Math.Max(1, _seq - RingCapacity + 1);
        if (last + 1 < oldest)
        {
            subscription.Offer(ProtocolCodec.Event(
                ProtocolEvents.SubscriberPaused,
                last,
                new SubscriberPausedData { Dropped = oldest - last - 1 },
                ProtocolJsonContext.Default.SubscriberPausedData));
            last = oldest - 1;
        }

        for (long seq = last + 1; seq <= _seq; seq++)
        {
            if (_ring[seq % RingCapacity] is { } message)
            {
                subscription.Offer(message);
            }
        }
    }

    private void Unsubscribe(EventSubscription subscription)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscription);
        }
    }
}
