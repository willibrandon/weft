using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Operates controls through their UI Automation patterns, as screen readers and automation clients do.
/// </summary>
internal static class Automation
{
    /// <summary>
    /// Gets the name automation clients read for a control.
    /// </summary>
    /// <param name="element">The control.</param>
    /// <returns>The name, or an empty string.</returns>
    internal static string NameOf(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return AutomationProperties.GetName(element) is { Length: > 0 } name ? name
            : element switch
            {
                MenuFlyoutItem item => item.Text,
                MenuFlyoutSubItem item => item.Text,
                TabViewItem { Header: string header } => header,
                ContentControl { Content: string text } => text,
                _ => string.Empty
            };
    }

    /// <summary>
    /// Invokes a button or menu item.
    /// </summary>
    /// <param name="element">The control.</param>
    internal static void Invoke(UIElement element)
    {
        Pattern<IInvokeProvider>(element, PatternInterface.Invoke).Invoke();
    }

    /// <summary>
    /// Activates a menu item the way a screen reader does: invoking, toggling, or selecting it.
    /// </summary>
    /// <param name="element">The item.</param>
    internal static void Activate(UIElement element)
    {
        AutomationPeer peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
        else if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection)
        {
            selection.Select();
        }
        else
        {
            Pattern<IToggleProvider>(element, PatternInterface.Toggle).Toggle();
        }
    }

    /// <summary>
    /// Selects an item, such as a tab.
    /// </summary>
    /// <param name="element">The item.</param>
    internal static void Select(UIElement element)
    {
        Pattern<ISelectionItemProvider>(element, PatternInterface.SelectionItem).Select();
    }

    /// <summary>
    /// Expands a submenu or other expandable control.
    /// </summary>
    /// <param name="element">The control.</param>
    internal static void Expand(UIElement element)
    {
        Pattern<IExpandCollapseProvider>(element, PatternInterface.ExpandCollapse).Expand();
    }

    /// <summary>
    /// Sets a range control, such as a scroll bar or number box, to a value.
    /// </summary>
    /// <param name="element">The control.</param>
    /// <param name="value">The value.</param>
    internal static void SetRange(UIElement element, double value)
    {
        Pattern<IRangeValueProvider>(element, PatternInterface.RangeValue).SetValue(value);
    }

    /// <summary>
    /// Gets a control pattern, failing the test when the control does not support it.
    /// </summary>
    /// <typeparam name="T">The provider interface.</typeparam>
    /// <param name="element">The control.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The provider.</returns>
    internal static T Pattern<T>(UIElement element, PatternInterface pattern)
        where T : class
    {
        AutomationPeer peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
        return peer.GetPattern(pattern) as T
            ?? throw new AssertFailedException(element.GetType().Name + " does not support " + pattern + ".");
    }
}
