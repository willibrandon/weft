using System.Diagnostics;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Runs a command in a new block and reports its exit code and output.
/// </summary>
internal static class BlockRunner
{
    /// <summary>
    /// Runs a command, waiting up to the timeout for it to exit.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="tab">The tab to run in.</param>
    /// <param name="parameters">The run parameters.</param>
    /// <param name="cancellationToken">Cancels the wait, not the process.</param>
    /// <returns>The result.</returns>
    internal static async Task<BlockRunResult> RunAsync(SessionRegistry registry, Tab tab, BlockRunParams parameters, CancellationToken cancellationToken)
    {
        if (parameters.Command.Count == 0)
        {
            throw new ProtocolException(ErrorCodes.InvalidParams, "A command is required.");
        }

        long started = Stopwatch.GetTimestamp();
        Block anchor = tab.Active ?? throw new ProtocolException(ErrorCodes.Unavailable, "The tab has no blocks.");
        Block block;
        try
        {
            block = await registry.SplitBlockAsync(anchor, SplitOrientation.TopBottom, null, false, parameters.Command, parameters.Cwd, focus: false, keepOnExit: true, cancellationToken).ConfigureAwait(false);
        }
        catch (ProtocolException exception) when (string.Equals(exception.Code, ErrorCodes.Unavailable, StringComparison.Ordinal) && !exception.Message.Contains("Could not start", StringComparison.Ordinal))
        {
            Tab created = await registry.CreateTabAsync(tab.Session, null, parameters.Cwd, parameters.Command, cancellationToken).ConfigureAwait(false);
            block = created.Active ?? throw new ProtocolException(ErrorCodes.Unavailable, "The command block could not be created.");
            block.KeepOnExit = true;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1, parameters.TimeoutMs));
        bool completed = false;
        try
        {
            while (block.State is BlockState.Starting or BlockState.Running)
            {
                await block.StateChanged.WaitAsync(timeout.Token).ConfigureAwait(false);
            }

            completed = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        string text = string.Empty;
        if (block.Host is { } host)
        {
            BlockCapture capture = host.Capture(int.MaxValue, CaptureFormat.Text);
            List<string> lines = [.. capture.Lines];
            while (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            text = string.Join('\n', lines);
        }

        (string output, bool truncated, long total, long omitted) = HeadTailBuffer.Truncate(text, parameters.OutputBytesCap);
        var result = new BlockRunResult
        {
            Block = block.Id.ToString(),
            Completed = completed,
            ExitCode = block.ExitCode,
            Output = output,
            Truncated = truncated,
            TotalBytes = total,
            OmittedBytes = omitted,
            DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds
        };

        if (completed && parameters.Close)
        {
            await registry.CloseBlockAsync(block, cancellationToken).ConfigureAwait(false);
        }
        else if (!completed)
        {
            block.KeepOnExit = false;
        }

        return result;
    }
}
