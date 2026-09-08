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
}
