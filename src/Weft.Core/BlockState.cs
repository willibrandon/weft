namespace Weft.Core;

/// <summary>
/// The lifecycle state of a block's process.
/// </summary>
public enum BlockState
{
    /// <summary>
    /// The pseudo-terminal is being created and the command has not started.
    /// </summary>
    Starting,

    /// <summary>
    /// The command is running.
    /// </summary>
    Running,

    /// <summary>
    /// The command has exited; the block keeps its screen and exit code until closed.
    /// </summary>
    Exited,

    /// <summary>
    /// The block has been removed from its tab.
    /// </summary>
    Closed
}
