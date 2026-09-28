using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Finds controls in a window and its open popups, which is where menus, flyouts, and dialogs appear.
/// </summary>
internal static class VisualTree
{
    /// <summary>
    /// Enumerates an element and everything below it, depth first.
    /// </summary>
    /// <param name="root">The root element.</param>
    /// <returns>The elements.</returns>
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            DependencyObject current = pending.Pop();
            yield return current;
            for (int index = VisualTreeHelper.GetChildrenCount(current) - 1; index >= 0; index--)
            {
                pending.Push(VisualTreeHelper.GetChild(current, index));
            }
        }
    }

    /// <summary>
    /// Enumerates the contents of every popup open over a window's content.
    /// </summary>
    /// <param name="root">The window's XAML root.</param>
    /// <returns>The elements.</returns>
    internal static IEnumerable<DependencyObject> Popups(XamlRoot root)
    {
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
            .Where(popup => popup.Child is not null)
            .SelectMany(popup => Descendants(popup.Child));
    }

    /// <summary>
    /// Enumerates a window's content and its open popups.
    /// </summary>
    /// <param name="content">The window's content.</param>
    /// <returns>The elements.</returns>
    internal static IEnumerable<DependencyObject> Everything(UIElement content)
    {
        return content.XamlRoot is { } root ? Descendants(content).Concat(Popups(root)) : Descendants(content);
    }

    /// <summary>
    /// Gets whether an element is open in a popup rather than in the window's own tree.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>Whether a popup contains it.</returns>
    internal static bool IsInPopup(FrameworkElement element)
    {
        return element.XamlRoot is { } root
            && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is { } child
                && Descendants(child).Contains(element));
    }

    /// <summary>
    /// Gets the open popups over a window's content.
    /// </summary>
    /// <param name="root">The window's XAML root.</param>
    /// <returns>The popups.</returns>
    internal static IReadOnlyList<Popup> OpenPopups(XamlRoot root)
    {
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(root);
    }
}
