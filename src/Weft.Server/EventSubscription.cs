using System.Threading.Channels;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// One subscriber's bounded queue of events, with pause markers instead of disconnection when it falls behind.
/// </summary>
internal sealed class EventSubscription : IDisposable
{
    private const int Capacity = 1024;
    private readonly Channel<ProtocolMessage> _channel = Channel.CreateBounded<ProtocolMessage>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropWrite
    });
    private readonly Action<EventSubscription> _onDispose;
    private long _dropped;

    /// <summary>
    /// Initializes a subscription.
    /// </summary>
    /// <param name="onDispose">Called when the subscription is disposed.</param>
    internal EventSubscription(Action<EventSubscription> onDispose)
    {
        _onDispose = onDispose;
    }

    /// <summary>
    /// Gets the reader the connection drains.
    /// </summary>
    internal ChannelReader<ProtocolMessage> Reader => _channel.Reader;

    /// <summary>
    /// Offers an event; when the queue is full the event is dropped and counted.
    /// </summary>
    /// <param name="message">The event.</param>
    internal void Offer(ProtocolMessage message)
    {
        if (Interlocked.Read(ref _dropped) > 0 && _channel.Reader.Count < Capacity / 2)
        {
            long dropped = Interlocked.Exchange(ref _dropped, 0);
            _channel.Writer.TryWrite(ProtocolCodec.Event(
                ProtocolEvents.SubscriberPaused,
                message.Seq ?? 0,
                new SubscriberPausedData { Dropped = dropped },
                ProtocolJsonContext.Default.SubscriberPausedData));
            _channel.Writer.TryWrite(new ProtocolMessage { Event = ProtocolEvents.SubscriberResumed, Seq = message.Seq });
        }

        if (!_channel.Writer.TryWrite(message))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// Completes the queue.
    /// </summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _onDispose(this);
    }
}
