namespace Weft.Desktop.Windows;

/// <summary>
/// A tab, session, or pane shown in window chrome.
/// </summary>
/// <param name="Id">The stable server identifier.</param>
/// <param name="Name">The untrusted name, displayed as plain text.</param>
/// <param name="SearchQuery">The pane's current search text.</param>
/// <param name="SearchMatches">The pane's matching row count.</param>
internal sealed record ChromeItem(string Id, string Name, string SearchQuery = "", int SearchMatches = 0);
