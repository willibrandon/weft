using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Weft.Client;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

namespace Weft.Desktop.Windows;

/// <summary>
/// Searches the same actions shown in menus without consuming terminal keystrokes.
/// </summary>
/// <remarks>
/// The owning window keeps one panel open at a time and restores the previous focus when it closes.
/// </remarks>
internal static class CommandPalette
{
    /// <summary>
    /// Renders the searchable command dialog.
    /// </summary>
    /// <param name="context">The window state and callbacks.</param>
    /// <returns>The dialog element.</returns>
    internal static Element Render(DialogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string query = context.Text.Trim();
        DesktopAction[] matches =
        [
            .. context.Chrome.Commands.Where(action => action.Id != "commands" && context.Chrome.CanPerform(action)
                && (query.Length == 0 || action.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || action.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        ];
        int index = matches.Length == 0 ? -1 : Math.Clamp(context.Selected, 0, matches.Length - 1);
        void Execute(int item)
        {
            if (item >= 0 && item < matches.Length)
            {
                string id = matches[item].Id;
                context.Close();
                context.Run(id);
            }
        }

        Element[] rows =
        [
            .. matches.Select(action => FlexRow(
                    TextBlock(action.Label).Flex(grow: 1),
                    TextBlock(WindowsShortcuts.Display(action.Id)).Opacity(0.6))
                .WithKey(action.Id))
        ];
        Element list = ListView((int?)index, value =>
            {
                if (value >= 0)
                {
                    context.SetSelected(value);
                }
            }, rows)
            .ItemClick(Execute)
            .AutomationName("Matching commands")
            .Height(300);
        Element search = TextBox(context.Text, value =>
            {
                context.SetText(value);
                context.SetSelected(0);
            }, placeholderText: "Search commands")
            .AutomationName("Search commands")
            .OnPreviewKeyDown((_, e) =>
            {
                if (e.Key is VirtualKey.Down or VirtualKey.Up && matches.Length != 0)
                {
                    context.SetSelected(Math.Clamp(index + (e.Key == VirtualKey.Down ? 1 : -1), 0, matches.Length - 1));
                    e.Handled = true;
                }
                else if (e.Key == VirtualKey.Enter)
                {
                    Execute(index);
                    e.Handled = true;
                }
            })
            .Ref(context.Field);
        return ContentDialog("Commands", VStack(8, search, list).Width(440), "Run") with
        {
            IsOpen = true,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = index >= 0,
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary)
                {
                    Execute(index);
                }
                else
                {
                    context.Close();
                }
            }
        };
    }
}
