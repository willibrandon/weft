using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Parses target strings from requests.
/// </summary>
internal static class Targets
{
    /// <summary>
    /// Parses a target string, treating null or empty as the current target.
    /// </summary>
    /// <param name="target">The target text.</param>
    /// <returns>The selector.</returns>
    /// <exception cref="ProtocolException">The text is malformed.</exception>
    internal static TargetSelector Parse(string? target)
    {
        if (!TargetSelector.TryParse(target, out TargetSelector selector))
        {
            throw new ProtocolException(ErrorCodes.InvalidParams, "Malformed target " + target + ".");
        }

        return selector;
    }

    /// <summary>
    /// Resolves a session, resurrecting a stored session when the name is not running.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="target">The target text.</param>
    /// <param name="cancellationToken">Cancels resurrection.</param>
    /// <returns>The session.</returns>
    internal static async Task<Session> SessionAsync(SessionRegistry registry, string? target, CancellationToken cancellationToken)
    {
        TargetSelector selector = Parse(target);
        await registry.EnsureRunningAsync(selector, cancellationToken).ConfigureAwait(false);
        return registry.ResolveSession(selector);
    }

    /// <summary>
    /// Resolves a tab, resurrecting a stored session when the name is not running.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="target">The target text.</param>
    /// <param name="cancellationToken">Cancels resurrection.</param>
    /// <returns>The tab.</returns>
    internal static async Task<Tab> TabAsync(SessionRegistry registry, string? target, CancellationToken cancellationToken)
    {
        TargetSelector selector = Parse(target);
        await registry.EnsureRunningAsync(selector, cancellationToken).ConfigureAwait(false);
        return registry.ResolveTab(selector);
    }

    /// <summary>
    /// Resolves a block, resurrecting a stored session when the name is not running.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="target">The target text.</param>
    /// <param name="cancellationToken">Cancels resurrection.</param>
    /// <returns>The block.</returns>
    internal static async Task<Block> BlockAsync(SessionRegistry registry, string? target, CancellationToken cancellationToken)
    {
        TargetSelector selector = Parse(target);
        await registry.EnsureRunningAsync(selector, cancellationToken).ConfigureAwait(false);
        return registry.ResolveBlock(selector);
    }
}
