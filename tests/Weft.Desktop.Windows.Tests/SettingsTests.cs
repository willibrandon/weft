using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Weft.Client;
using Windows.Foundation;
using Windows.UI;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Changes fonts, colors, and shortcuts through Settings and the View menu, and checks the terminal follows.
/// </summary>
[TestClass]
public sealed class SettingsTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The font size field in Settings changes the terminal's font and grid.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task FontSizeFromSettingsResizesTheGrid()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            int columns = window.Active.Width;
            ReactorWindow settings = OpenSettings();
            try
            {
                UIElement content = settings.NativeWindow.Content;
                NumberBox size = await FindAsync<NumberBox>(window, content, "Font size").ConfigureAwait(true);
                Automation.SetRange(size, 20);
                await window.UntilAsync(() => PreferencesStore.Current.FontSize is { } size && TestWindow.Near(size, 20)
                    && TestWindow.Near(window.Surface.Font.Size, 20))
                    .ConfigureAwait(true);
                _ = await window.WaitAsync(frame => frame.Blocks[0].Width < columns).ConfigureAwait(true);
            }
            finally
            {
                settings.Close();
                PreferencesStore.Update(preferences => preferences with { FontSize = null });
            }

            _ = await window.WaitAsync(frame => frame.Blocks[0].Width == columns).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// Larger Text and Smaller Text in the View menu step the font size and the grid follows.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task ViewMenuStepsTheFontSize()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            float initial = window.Surface.Font.Size;
            int cells = window.Active.Width * window.Active.Height;
            try
            {
                await window.MenuAsync("View", "Larger Text").ConfigureAwait(true);
                await window.UntilAsync(() => TestWindow.Near(window.Surface.Font.Size, initial + 1))
                    .ConfigureAwait(true);
                _ = await window.WaitAsync(frame => frame.Blocks[0].Width * frame.Blocks[0].Height < cells)
                    .ConfigureAwait(true);
                await window.MenuAsync("View", "Smaller Text").ConfigureAwait(true);
                await window.UntilAsync(() => TestWindow.Near(window.Surface.Font.Size, initial)).ConfigureAwait(true);
                _ = await window.WaitAsync(frame => frame.Blocks[0].Width * frame.Blocks[0].Height == cells)
                    .ConfigureAwait(true);
            }
            finally
            {
                PreferencesStore.Update(preferences => preferences with { FontSize = null });
            }
        });
    }

    /// <summary>
    /// A background color preference repaints the terminal without restarting it.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task BackgroundColorRepaintsTheTerminal()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            DesktopBlockFrame block = window.Active;
            Point empty = window.CellCenter(block, ((block.Height - 2) * block.Width) + (block.Width / 2));
            try
            {
                PreferencesStore.Update(preferences => preferences with { Background = "#203040" });
                await window.UntilAsync(() => TerminalAppearance.Background == Color.FromArgb(255, 0x20, 0x30, 0x40))
                    .ConfigureAwait(true);
                Color painted = window.PixelAt(await window.CaptureAsync().ConfigureAwait(true), empty);
                Assert.IsLessThan(6, TestWindow.Distance(painted, Color.FromArgb(255, 0x20, 0x30, 0x40)),
                    "The terminal kept its old background: " + painted + ".");
            }
            finally
            {
                PreferencesStore.Update(preferences => preferences with { Background = null });
            }
        });
    }

    /// <summary>
    /// Settings shows a command's shortcut, removes it, and restores the defaults.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task ShortcutsAreRemovedAndRestored()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            ReactorWindow settings = OpenSettings();
            try
            {
                UIElement content = settings.NativeWindow.Content;
                ComboBox command = await FindAsync<ComboBox>(window, content, "Command to customize")
                    .ConfigureAwait(true);
                command.SelectedIndex = DesktopActions.All.ToList().FindIndex(action => action.Id == "newTab");
                TextBlock current = await FindAsync<TextBlock>(window, content, "Current shortcut")
                    .ConfigureAwait(true);
                await window.UntilAsync(() => current.Text == "Ctrl+Shift+T").ConfigureAwait(true);

                Automation.Invoke(await FindAsync<Button>(window, content, "Remove Shortcut").ConfigureAwait(true));
                await window.UntilAsync(() => current.Text == "No shortcut"
                    && WindowsShortcuts.Value("newTab").Length == 0).ConfigureAwait(true);
                Automation.Invoke(await FindAsync<Button>(window, content, "Restore Defaults").ConfigureAwait(true));
                await window.UntilAsync(() => current.Text == "Ctrl+Shift+T"
                    && WindowsShortcuts.Value("newTab") == "ctrl+shift+t").ConfigureAwait(true);
            }
            finally
            {
                settings.Close();
                WindowsShortcuts.Restore();
            }
        });
    }

    private static ReactorWindow OpenSettings()
    {
        // Settings opens in the background here, as terminal windows do, so tests never take the keyboard.
        ReactorWindow settings = ReactorApp.OpenWindow(new WindowSpec
        {
            Title = "Weft Settings",
            Width = 560,
            Height = 640,
            ActivateOnOpen = false
        }, static () => new SettingsView());
        settings.AppWindow.Show(activateWindow: false);
        return settings;
    }

    private static async Task<T> FindAsync<T>(TestWindow window, UIElement content, string name)
        where T : FrameworkElement
    {
        T? found = null;
        await window.UntilAsync(() => (found = VisualTree.Everything(content).OfType<T>()
            .FirstOrDefault(element => Automation.NameOf(element) == name)) is not null).ConfigureAwait(true);
        return found!;
    }
}
