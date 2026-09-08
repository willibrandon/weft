using System.Text.RegularExpressions;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Waits on a block for a pattern, an exit, or a revision change, waking on output rather than polling.
/// </summary>
internal static class BlockWaiter
{
    /// <summary>
    /// Waits until a condition holds or the timeout elapses.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="parameters">The wait parameters.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The outcome.</returns>
    internal static async ValueTask<BlockWaitResult> WaitAsync(Block block, BlockWaitParams parameters, CancellationToken cancellationToken)
    {
        Regex? regex = null;
        if (parameters.Pattern is { Length: > 0 } pattern)
        {
            try
            {
                regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException exception)
            {
                throw new ProtocolException(ErrorCodes.InvalidParams, "Invalid pattern: " + exception.Message);
            }
        }

        long? sinceRevision = parameters.Revision;
        if (regex is null && !parameters.Exit && sinceRevision is null)
        {
            sinceRevision = block.Revision;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1, parameters.TimeoutMs));
        try
        {
            while (true)
            {
                BlockHost? host = block.Host;

                // Subscribe before looking, so output applied between the capture and the wait still wakes the loop.
                Task? outputChanged = host?.OutputChanged.WaitAsync(timeout.Token);
                Task stateChanged = block.StateChanged.WaitAsync(timeout.Token);
                long revision = block.Revision;
                if (block.State is BlockState.Exited or BlockState.Closed && parameters.Exit)
                {
                    return new BlockWaitResult { Outcome = WaitOutcome.Exit, Revision = revision, ExitCode = block.ExitCode };
                }

                if (sinceRevision is { } since && revision > since)
                {
                    return new BlockWaitResult { Outcome = WaitOutcome.Changed, Revision = revision, ExitCode = block.ExitCode };
                }

                if (regex is not null && host is not null)
                {
                    BlockCapture capture = host.Capture(0, CaptureFormat.Text);
                    foreach (string line in capture.Lines)
                    {
                        Match match = regex.Match(line);
                        if (match.Success)
                        {
                            return new BlockWaitResult { Outcome = WaitOutcome.Pattern, Revision = capture.Revision, Match = match.Value, Line = line, ExitCode = block.ExitCode };
                        }
                    }
                }

                if (host is null)
                {
                    return new BlockWaitResult { Outcome = WaitOutcome.Exit, Revision = revision, ExitCode = block.ExitCode };
                }

                await Task.WhenAny(outputChanged!, stateChanged).ConfigureAwait(false);

                if (timeout.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new BlockWaitResult { Outcome = WaitOutcome.Timeout, Revision = block.Revision, ExitCode = block.ExitCode };
                }
            }
        }
        finally
        {
            // Cancelling releases the signal subscriptions taken for the last look, so an early return leaves nothing behind.
            await timeout.CancelAsync().ConfigureAwait(false);
        }
    }
}
