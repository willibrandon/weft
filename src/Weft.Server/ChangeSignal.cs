namespace Weft.Server;

/// <summary>
/// Wakes every waiter once per notification, for waits that poll state between output batches.
/// </summary>
internal sealed class ChangeSignal
{
    private readonly Lock _gate = new();
    private TaskCompletionSource _current = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Returns a task that completes on the next notification.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    internal Task WaitAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_gate)
        {
            task = _current.Task;
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Completes the current waiters and starts a new wait generation.
    /// </summary>
    internal void Notify()
    {
        TaskCompletionSource previous;
        lock (_gate)
        {
            previous = _current;
            _current = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        previous.TrySetResult();
    }
}
