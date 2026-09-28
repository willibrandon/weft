using Microsoft.UI.Reactor;

namespace Weft.Desktop.Windows;

/// <summary>
/// Opens terminal windows and the single Settings window; closing either leaves sessions running.
/// </summary>
internal static class DesktopWindows
{
    private static readonly List<ReactorWindow> s_terminals = [];
    private static ReactorWindow? s_settings;

    /// <summary>
    /// Gets whether windows run in isolation for tests, without saving placement or reading user preferences.
    /// </summary>
    internal static bool Isolated { get; } =
        Environment.GetEnvironmentVariable("WEFT_DESKTOP_PREFERENCES") is { Length: > 0 };

    /// <summary>
    /// Gets the window description for a terminal; the first window restores the saved placement.
    /// </summary>
    /// <param name="background">Whether the window opens behind the active window without taking the keyboard.</param>
    /// <returns>The window description.</returns>
    internal static WindowSpec TerminalSpec(bool background = false)
    {
        var spec = new WindowSpec
        {
            Title = "Weft",
            Width = 1080,
            Height = 720,
            MinWidth = 520,
            MinHeight = 280,
            ExtendsContentIntoTitleBar = true,
            TitleBarHeight = WindowTitleBarHeight.Tall,
            ActivateOnOpen = !background
        };
        return Isolated || ReactorApp.Windows.Count != 0
            ? spec
            : spec.WithPersistence("weft-terminal", WindowStartPosition.CenterOnCurrent);
    }

    /// <summary>
    /// Opens another terminal window attached to the most recent session.
    /// </summary>
    /// <param name="background">Whether the window opens behind the active window without taking the keyboard.</param>
    /// <returns>The window.</returns>
    internal static ReactorWindow OpenTerminal(bool background = false)
    {
        // Reactor disposes each window when it closes; the list only records which windows are open.
        ReactorWindow window = ReactorApp.OpenWindow(TerminalSpec(background), static () => new TerminalWindow());
        if (background)
        {
            // Windows show when activated, so a background window is shown without activation instead.
            window.AppWindow.Show(activateWindow: false);
        }

        s_terminals.Add(window);
        window.Closed += (_, _) => s_terminals.Remove(window);
        return window;
    }

    /// <summary>
    /// Shows the Settings window, activating it when it is already open.
    /// </summary>
    internal static void ShowSettings()
    {
        if (s_settings is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        s_settings = ReactorApp.OpenWindow(new WindowSpec
        {
            Title = "Weft Settings",
            Width = 560,
            Height = 640,
            MinWidth = 440,
            MinHeight = 420,
            IsMaximizable = false
        }, static () => new SettingsView());
        s_settings.Closed += (_, _) => s_settings = null;
    }

    /// <summary>
    /// Closes every window and exits; server-owned sessions and processes keep running.
    /// </summary>
    internal static void Exit()
    {
        ReactorApp.Exit();
    }
}
