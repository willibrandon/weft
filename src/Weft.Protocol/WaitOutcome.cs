namespace Weft.Protocol;

/// <summary>
/// Why a block.wait returned.
/// </summary>
public enum WaitOutcome
{
    /// <summary>
    /// The pattern matched a screen line.
    /// </summary>
    Pattern,

    /// <summary>
    /// The block's process exited.
    /// </summary>
    Exit,

    /// <summary>
    /// The block's revision advanced past the requested revision.
    /// </summary>
    Changed,

    /// <summary>
    /// The timeout elapsed.
    /// </summary>
    Timeout
}
