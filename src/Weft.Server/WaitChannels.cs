namespace Weft.Server;

/// <summary>
/// Named channels scripts synchronize on: waiting blocks until a signal, and signals wake one waiter or persist.
/// </summary>
internal sealed class WaitChannels
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<TaskCompletionSource>> _waiters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _signalled = new(StringComparer.Ordinal);

    /// <summary>
    /// Waits until the channel is signalled.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes on signal.</returns>
    internal Task WaitAsync(string channel, CancellationToken cancellationToken)
    {
        TaskCompletionSource source;
        lock (_gate)
        {
            if (_signalled.Remove(channel))
            {
                return Task.CompletedTask;
            }

            source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_waiters.TryGetValue(channel, out Queue<TaskCompletionSource>? queue))
            {
                queue = new Queue<TaskCompletionSource>();
                _waiters[channel] = queue;
            }

            queue.Enqueue(source);
        }

        return source.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Signals a channel, waking the oldest waiter or recording the signal for the next wait.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    internal void Signal(string channel)
    {
        TaskCompletionSource? source = null;
        lock (_gate)
        {
            if (_waiters.TryGetValue(channel, out Queue<TaskCompletionSource>? queue) && queue.Count > 0)
            {
                source = queue.Dequeue();
                if (queue.Count == 0)
                {
                    _waiters.Remove(channel);
                }
            }
            else
            {
                _signalled.Add(channel);
            }
        }

        source?.TrySetResult();
    }
}
