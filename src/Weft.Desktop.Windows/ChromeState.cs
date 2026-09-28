using Weft.Client;

namespace Weft.Desktop.Windows;

/// <summary>
/// The parts of a frame that window chrome shows, compared by value so output alone does not re-render chrome.
/// </summary>
/// <param name="Connected">Whether commands can be sent.</param>
/// <param name="Title">The session name.</param>
/// <param name="Error">The latest failure, if any.</param>
/// <param name="ActiveSession">The attached session.</param>
/// <param name="ActiveTab">The selected tab.</param>
/// <param name="ActiveBlock">The focused pane.</param>
/// <param name="Tabs">The session's tabs.</param>
/// <param name="Sessions">The server's sessions.</param>
/// <param name="Blocks">The visible panes.</param>
/// <param name="Commands">The shared action catalog.</param>
internal sealed record ChromeState(bool Connected, string Title, string? Error, string? ActiveSession, string? ActiveTab,
    string? ActiveBlock, IReadOnlyList<ChromeItem> Tabs, IReadOnlyList<ChromeItem> Sessions, IReadOnlyList<ChromeItem> Blocks,
    IReadOnlyList<DesktopAction> Commands)
{
    /// <summary>
    /// Gets the state shown before the first frame.
    /// </summary>
    internal static ChromeState Connecting { get; } = new(false, "Weft", null, null, null, null,
        Array.Empty<ChromeItem>(), Array.Empty<ChromeItem>(), Array.Empty<ChromeItem>(), DesktopActions.All);

    /// <summary>
    /// Projects a frame.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The chrome state.</returns>
    internal static ChromeState From(DesktopFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new ChromeState(frame.Connected, frame.Title, frame.Error, frame.ActiveSession, frame.ActiveTab,
            frame.Blocks.FirstOrDefault(block => block.Active)?.Id,
            frame.Tabs.Select(tab => new ChromeItem(tab.Id, tab.Name)).ToArray(),
            frame.Sessions.Select(session => new ChromeItem(session.Id, session.Name)).ToArray(),
            frame.Blocks.Select(block => new ChromeItem(block.Id, block.Title, block.SearchQuery, block.SearchMatches)).ToArray(),
            frame.Commands);
    }

    /// <summary>
    /// Gets whether an action applies to the current window state.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>Whether it can run.</returns>
    internal bool CanPerform(DesktopAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.Id is "commands" or "font" || (Connected && action.Scope switch
        {
            "session" => ActiveSession is not null,
            "tab" => ActiveTab is not null,
            "block" => ActiveBlock is not null,
            _ => true
        });
    }

    /// <inheritdoc />
    public bool Equals(ChromeState? other)
    {
        return other is not null && Connected == other.Connected && Title == other.Title && Error == other.Error
            && ActiveSession == other.ActiveSession && ActiveTab == other.ActiveTab && ActiveBlock == other.ActiveBlock
            && Tabs.SequenceEqual(other.Tabs) && Sessions.SequenceEqual(other.Sessions) && Blocks.SequenceEqual(other.Blocks)
            && Commands.SequenceEqual(other.Commands);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Connected, Title, Error, ActiveSession, ActiveTab, ActiveBlock, Tabs.Count, Sessions.Count);
    }
}
