using System.Threading.Channels;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Serializes synchronized input for one tab so every sibling receives bytes in source order.
/// </summary>
internal sealed class SyncInputQueue : IDisposable
{
    private static readonly TimeSpan s_writeTimeout = TimeSpan.FromSeconds(5);
    private readonly Channel<SyncInputItem> _items = Channel.CreateUnbounded<SyncInputItem>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>
    /// Creates the queue and starts the pump that drains it.
    /// </summary>
    internal SyncInputQueue()
    {
        _ = PumpAsync();
    }

    /// <summary>
    /// Queues input for the targets and returns a task that completes once it has been written.
    /// </summary>
    /// <param name="targets">The hosts to write to.</param>
    /// <param name="bytes">The encoded input.</param>
    /// <param name="pasteText">Text to paste with per-target bracketing, or null for raw bytes.</param>
    /// <param name="cancellationToken">Stops waiting for the write; the write itself still happens in order.</param>
    /// <returns>A task that completes when every target has been written or skipped.</returns>
    internal Task EnqueueAsync(IReadOnlyList<BlockHost> targets, ReadOnlyMemory<byte> bytes, string? pasteText, CancellationToken cancellationToken)
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _items.Writer.TryWrite(new SyncInputItem(targets, bytes, pasteText, done))
            ? done.Task.WaitAsync(cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Completes the queue; input already queued is still written.
    /// </summary>
    public void Dispose() => _items.Writer.TryComplete();

    private static async Task WriteAsync(BlockHost host, SyncInputItem item)
    {
        ReadOnlyMemory<byte> bytes = item.PasteText is { } text
            ? host.Capture(0, CaptureFormat.Text).BracketedPaste ? KeyEncoder.EncodeBracketedPaste(text) : KeyEncoder.EncodeText(text)
            : item.Bytes;

        // A sibling that has stopped reading its input, say one paused with Ctrl+S, must not stall the tab.
        using CancellationTokenSource timeout = new(s_writeTimeout);
        try
        {
            await host.WriteInputAsync(bytes, timeout.Token).AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ServerLog.Warn("Synchronized input to a sibling timed out and was skipped.");
        }
        catch (IOException exception)
        {
            ServerLog.Warn("Synchronized input failed: " + exception.Message);
        }
        catch (ObjectDisposedException)
        {
            ServerLog.Debug("SyncInputQueue ignored ObjectDisposedException.");
        }
    }

    private async Task PumpAsync()
    {
        await foreach (SyncInputItem item in _items.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            foreach (BlockHost host in item.Targets)
            {
                await WriteAsync(host, item).ConfigureAwait(false);
            }

            item.Done.TrySetResult();
        }
    }
}
