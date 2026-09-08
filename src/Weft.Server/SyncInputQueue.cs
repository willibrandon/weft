namespace Weft.Server;

/// <summary>
/// Routes synchronized input for one tab to a serial queue per sibling, in arrival order.
/// </summary>
/// <remarks>
/// Order only matters per target, so each sibling gets its own queue. A sibling that stops reading
/// then stalls only its own queue; the others keep receiving input at full speed.
/// </remarks>
internal sealed class SyncInputQueue : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<BlockHost, SyncInputTarget> _targets = [];

    /// <summary>
    /// Queues input for the targets and returns a task that completes once every target has taken it or dropped it.
    /// </summary>
    /// <param name="targets">The hosts to write to.</param>
    /// <param name="bytes">The encoded input.</param>
    /// <param name="pasteText">Text to paste with per-target bracketing, or null for raw bytes.</param>
    /// <param name="cancellationToken">Stops waiting; the writes themselves still happen in order.</param>
    /// <returns>A task that completes when the input has been handled everywhere.</returns>
    internal Task EnqueueAsync(IReadOnlyList<BlockHost> targets, ReadOnlyMemory<byte> bytes, string? pasteText, CancellationToken cancellationToken)
    {
        var writes = new List<Task>(targets.Count);
        lock (_gate)
        {
            foreach (BlockHost host in targets)
            {
                if (!_targets.TryGetValue(host, out SyncInputTarget? target) || target.Dead)
                {
                    target?.Dispose();
                    target = new SyncInputTarget(host);
                    _targets[host] = target;
                }

                writes.Add(target.EnqueueAsync(bytes, pasteText));
            }
        }

        return Task.WhenAll(writes).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Completes every queue; input already queued is still written.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (SyncInputTarget target in _targets.Values)
            {
                target.Dispose();
            }

            _targets.Clear();
        }
    }
}
