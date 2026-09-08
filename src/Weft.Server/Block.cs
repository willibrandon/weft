using Weft.Core;

namespace Weft.Server;

/// <summary>
/// A block: one terminal inside a tab.
/// </summary>
internal sealed class Block
{
    /// <summary>
    /// Initializes a block record.
    /// </summary>
    /// <param name="id">The block id.</param>
    /// <param name="tab">The owning tab.</param>
    /// <param name="command">The command file name.</param>
    /// <param name="arguments">The command arguments.</param>
    /// <param name="cwd">The working directory.</param>
    internal Block(BlockId id, Tab tab, string command, IReadOnlyList<string> arguments, string cwd)
    {
        Id = id;
        Tab = tab;
        Command = command;
        Arguments = arguments;
        Cwd = cwd;
        Title = Path.GetFileName(command);
    }

    /// <summary>
    /// Gets the block id.
    /// </summary>
    internal BlockId Id { get; }

    /// <summary>
    /// Gets the owning tab.
    /// </summary>
    internal Tab Tab { get; }

    /// <summary>
    /// Gets the command file name.
    /// </summary>
    internal string Command { get; }

    /// <summary>
    /// Gets the command arguments.
    /// </summary>
    internal IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Gets the working directory.
    /// </summary>
    internal string Cwd { get; }

    /// <summary>
    /// Gets or sets the title reported by the terminal.
    /// </summary>
    internal string Title { get; set; }

    /// <summary>
    /// Gets or sets a title pinned by the user, which overrides the terminal title.
    /// </summary>
    internal string? PinnedTitle { get; set; }

    /// <summary>
    /// Gets the title to display.
    /// </summary>
    internal string DisplayTitle => string.IsNullOrEmpty(PinnedTitle) ? Title : PinnedTitle;

    /// <summary>
    /// Gets or sets the process state.
    /// </summary>
    internal BlockState State { get; set; } = BlockState.Starting;

    /// <summary>
    /// Gets or sets the exit code once exited.
    /// </summary>
    internal int? ExitCode { get; set; }

    /// <summary>
    /// Gets or sets whether the block stays after its process exits.
    /// </summary>
    internal bool KeepOnExit { get; set; }

    /// <summary>
    /// Gets or sets whether the block floats above the tiled layout.
    /// </summary>
    internal bool Floating { get; set; }

    /// <summary>
    /// Gets or sets the floating bounds.
    /// </summary>
    internal LayoutRect FloatingBounds { get; set; }

    /// <summary>
    /// Gets or sets the current terminal width.
    /// </summary>
    internal int Width { get; set; }

    /// <summary>
    /// Gets or sets the current terminal height.
    /// </summary>
    internal int Height { get; set; }

    /// <summary>
    /// Gets or sets the host once started.
    /// </summary>
    internal BlockHost? Host { get; set; }

    /// <summary>
    /// Gets the HMP1 socket path.
    /// </summary>
    internal string SocketPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the output revision.
    /// </summary>
    internal long Revision => Host?.Revision ?? 0;

    /// <summary>
    /// Gets the signal notified when the process state changes.
    /// </summary>
    internal ChangeSignal StateChanged { get; } = new();

    /// <summary>
    /// Gets or sets whether the block ignores synchronized input for its tab.
    /// </summary>
    internal bool ExcludedFromSync { get; set; }

    /// <summary>
    /// Gets or sets the tick count of the last published output event.
    /// </summary>
    internal long LastActivityPublished { get; set; }
}
