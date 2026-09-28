using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Weft.Client;
using Windows.System;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;

namespace Weft.Desktop.Windows;

/// <summary>
/// Edits app appearance and command shortcuts without changing shell configuration.
/// </summary>
internal sealed class SettingsView : Component
{
    private const string Guidance = "Shortcuts combine Shift with Ctrl or Alt so terminal keys stay available.";

    /// <inheritdoc />
    public override Element Render()
    {
        (int _, Action<int> setVersion) = UseState(0);
        (int command, Action<int> setCommand) = UseState(0);
        (bool recording, Action<bool> setRecording) = UseState(false);
        (string message, Action<string> setMessage) = UseState(Guidance);
        IReadOnlyList<string> families = UseMemo(TerminalFont.MonospacedFamilies, []);
        UseEffect(() =>
        {
            int version = 0;
            void Changed()
            {
                setVersion(++version);
            }

            PreferencesStore.Changed += Changed;
            return () => PreferencesStore.Changed -= Changed;
        }, []);

        DesktopPreferences preferences = PreferencesStore.Current;
        IReadOnlyList<DesktopAction> actions = DesktopActions.All;
        DesktopAction selected = actions[Math.Clamp(command, 0, actions.Count - 1)];
        string family = preferences.FontFamily ?? TerminalFont.BundledFamily;
        int familyIndex = Math.Max(0, families.ToList().FindIndex(name =>
            string.Equals(name, family, StringComparison.OrdinalIgnoreCase)));
        string shortcut = WindowsShortcuts.Display(selected.Id);

        void Record(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (!recording)
            {
                return;
            }

            e.Handled = true;
            if (e.Key == VirtualKey.Escape)
            {
                setRecording(false);
                setMessage(Guidance);
                return;
            }

            if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftControl
                or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftMenu
                or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows)
            {
                return;
            }

            if (!ShortcutChord.IsNameable(e.Key))
            {
                setMessage("That key cannot be used in a shortcut.");
                return;
            }

            var chord = new ShortcutChord(ShortcutChord.Normalize(e.Key), TerminalSurface.Modifiers());
            string? error = WindowsShortcuts.Save(chord, selected.Id, actions);
            setRecording(false);
            setMessage(error ?? (selected.Label.TrimEnd('…') + " now uses " + chord.Display() + "."));
        }

        return ScrollView(VStack(14,
            SubHeading("Appearance"),
            ColorRow("Background", TerminalAppearance.Background, color =>
                PreferencesStore.Update(value => value with { Background = TerminalAppearance.Format(color) })),
            ColorRow("Text", TerminalAppearance.Foreground, color =>
                PreferencesStore.Update(value => value with { Foreground = TerminalAppearance.Format(color) })),
            ColorRow("Cursor", TerminalAppearance.Cursor, color =>
                PreferencesStore.Update(value => value with { Cursor = TerminalAppearance.Format(color) })),
            ComboBox([.. families], familyIndex, index =>
                {
                    if (index >= 0 && index < families.Count)
                    {
                        string next = families[index];
                        PreferencesStore.Update(value =>
                            value with { FontFamily = next == TerminalFont.BundledFamily ? null : next });
                    }
                })
                .Header("Terminal font")
                .AutomationName("Terminal font")
                .Width(320),
            NumberBox(preferences.FontSize ?? TerminalFont.DefaultSize, size =>
                {
                    if (!double.IsNaN(size))
                    {
                        PreferencesStore.Update(value => value with { FontSize = Math.Clamp(size, 8, 40) });
                    }
                }, header: "Font size")
                .Range(8, 40)
                .SpinButtons()
                .AutomationName("Font size")
                .Width(160),
            TextBlock("Prompts and colors printed by your shell stay in your shell configuration.").Opacity(0.7)
                .TextWrapping(TextWrapping.Wrap),
            SubHeading("Shortcuts").Margin(0, 12, 0, 0),
            ComboBox([.. actions.Select(action => action.Label)], command, index =>
                {
                    setCommand(index);
                    setRecording(false);
                    setMessage(Guidance);
                })
                .Header("Command")
                .AutomationName("Command to customize")
                .Width(320),
            TextBlock(shortcut.Length == 0 ? "No shortcut" : shortcut).AutomationName("Current shortcut"),
            HStack(8,
                Button(recording ? "Press a shortcut…" : "Record Shortcut", () =>
                    {
                        setRecording(true);
                        setMessage("Press the new shortcut, or Esc to cancel.");
                    })
                    .AutomationName("Record shortcut")
                    .OnPreviewKeyDown(Record),
                Button("Remove Shortcut", () =>
                {
                    _ = WindowsShortcuts.Save(null, selected.Id, actions);
                    setMessage(selected.Label.TrimEnd('…') + " has no shortcut.");
                }),
                Button("Restore Defaults", () =>
                {
                    WindowsShortcuts.Restore();
                    setMessage("Default shortcuts restored.");
                })),
            TextBlock(message).FontSize(12).Opacity(0.8).TextWrapping(TextWrapping.Wrap)
                .AutomationName("Shortcut status"))
            .Padding(24))
            .RequestedTheme(ElementTheme.Dark)
            .Background(TerminalAppearance.Format(TerminalAppearance.Background));
    }

    private static StackElement ColorRow(string name, Color color, Action<Color> changed)
    {
        Element swatch = Border(Empty()).Width(44).Height(20).CornerRadius(4)
            .Background(TerminalAppearance.Format(color));
        return HStack(12,
            TextBlock(name).Width(90).VAlign(VerticalAlignment.Center),
            Button(swatch)
                .AutomationName(name + " color")
                .WithFlyout(ContentFlyout(ColorPicker(color, changed), FlyoutPlacementMode.Bottom)));
    }
}
