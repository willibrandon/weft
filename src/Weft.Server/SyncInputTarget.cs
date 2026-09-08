using System.Threading.Channels;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// A serial input queue for one synchronized sibling, so a sibling that stops reading holds up only itself.
/// </summary>
internal sealed class SyncInputTarget : IDisposable
{
    private const int Capacity = 256;
    private static readonly TimeSpan s_writeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_probeTimeout = TimeSpan.FromMilliseconds(250);
    private readonly BlockHost _host;
    private readonly Channel<SyncInputItem> _items;
    private bool _stalled;

    /// <summary>
    /// Creates the queue for a host and starts its pump.
    /// </summary>
    /// <param name="host">The sibling to write to.</param>
    internal SyncInputTarget(BlockHost host)
    {
        _host = host;
        _items = Channel.CreateBounded<SyncInputItem>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest },
            dropped => dropped.Done.TrySetResult());
        _ = PumpAsync();
    }

    /// <summary>
    /// Gets whether the host can no longer take input, so the queue should be replaced.
    /// </summary>
    internal bool Dead { get; private set; }

    /// <summary>
    /// Queues input and returns a task that completes once it has been written, skipped, or dropped.
    /// </summary>
    /// <param name="bytes">The encoded input.</param>
    /// <param name="pasteText">Text to paste with bracketing decided by the target, or null for raw bytes.</param>
    /// <returns>The completion task.</returns>
    internal Task EnqueueAsync(ReadOnlyMemory<byte> bytes, string? pasteText)
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_items.Writer.TryWrite(new SyncInputItem(bytes, pasteText, done)))
        {
            done.TrySetResult();
        }

        return done.Task;
    }

    /// <summary>
    /// Completes the queue; input already queued is still written.
    /// </summary>
    public void Dispose() => _items.Writer.TryComplete();

    private async Task PumpAsync()
    {
        await foreach (SyncInputItem item in _items.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await WriteAsync(item).ConfigureAwait(false);
            item.Done.TrySetResult();
        }
    }

    private async Task WriteAsync(SyncInputItem item)
    {
        if (Dead)
        {
            return;
        }

        ReadOnlyMemory<byte> bytes = item.PasteText is { } text
            ? _host.Capture(0, CaptureFormat.Text).BracketedPaste ? KeyEncoder.EncodeBracketedPaste(text) : KeyEncoder.EncodeText(text)
            : item.Bytes;

        // A sibling that has stopped reading, say one paused with Ctrl+S, gets a short probe per item after the
        // first timeout instead of the full budget, so its own backlog stays small and it resumes when it reads.
        using CancellationTokenSource timeout = new(_stalled ? s_probeTimeout : s_writeTimeout);
        try
        {
            await _host.WriteInputAsync(bytes, timeout.Token).AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
            if (_stalled)
            {
                _stalled = false;
                ServerLog.Info("Synchronized input to a sibling resumed.");
            }
        }
        catch (OperationCanceledException)
        {
            if (!_stalled)
            {
                _stalled = true;
                ServerLog.Warn("Synchronized input to a sibling timed out; it is probed briefly per key until it reads again.");
            }
        }
        catch (IOException exception)
        {
            ServerLog.Warn("Synchronized input failed: " + exception.Message);
            Dead = true;
        }
        catch (ObjectDisposedException)
        {
            Dead = true;
        }
    }
}
