using Weft.Client;
using Windows.System;

namespace Weft.Desktop.Windows;

/// <summary>
/// Applies Windows defaults and user overrides to shared catalog actions and fixed app commands.
/// </summary>
/// <remarks>
/// Defaults follow Windows terminal conventions. Plain Ctrl and Alt letters stay with the terminal,
/// so a recorded shortcut must combine Shift with Ctrl or Alt.
/// </remarks>
internal static class WindowsShortcuts
{
    /// <summary>
    /// Gets the default catalog shortcuts by action identifier.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Defaults { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["commands"] = "ctrl+shift+p",
            ["find"] = "ctrl+shift+f",
            ["newTab"] = "ctrl+shift+t",
            ["splitRight"] = "alt+shift+plus",
            ["splitBelow"] = "alt+shift+minus",
            ["nextTab"] = "ctrl+tab",
            ["previousTab"] = "ctrl+shift+tab"
        };

    /// <summary>
    /// Gets the app commands whose shortcuts are fixed and cannot be assigned to catalog actions.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Fixed { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["newWindow"] = "ctrl+shift+n",
            ["settings"] = "ctrl+comma",
            ["copy"] = "ctrl+shift+c",
            ["paste"] = "ctrl+shift+v",
            ["selectAll"] = "ctrl+shift+a",
            ["largerText"] = "ctrl+plus",
            ["smallerText"] = "ctrl+minus"
        };

    /// <summary>
    /// Gets the effective shortcut text for an action, which is empty when it has none.
    /// </summary>
    /// <param name="id">The action or app command identifier.</param>
    /// <returns>The stored chord text.</returns>
    internal static string Value(string id)
    {
        return Fixed.TryGetValue(id, out string? fixedValue) ? fixedValue
            : PreferencesStore.Current.Shortcuts.TryGetValue(id, out string? custom) ? custom
            : Defaults.GetValueOrDefault(id, string.Empty);
    }

    /// <summary>
    /// Gets the shortcut shown beside an action, such as <c>Ctrl+Shift+T</c>.
    /// </summary>
    /// <param name="id">The action identifier.</param>
    /// <returns>The display text, or empty when the action has no shortcut.</returns>
    internal static string Display(string id)
    {
        return ShortcutChord.Parse(Value(id))?.Display() ?? string.Empty;
    }

    /// <summary>
    /// Finds the catalog action or app command bound to a key press.
    /// </summary>
    /// <param name="chord">The pressed key and modifiers.</param>
    /// <param name="actions">The catalog actions.</param>
    /// <returns>The identifier, or null when the key belongs to the terminal.</returns>
    internal static string? Match(ShortcutChord chord, IReadOnlyList<DesktopAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        string? fixedId = Fixed.FirstOrDefault(entry => ShortcutChord.Parse(entry.Value) == chord).Key;
        return fixedId ?? actions.FirstOrDefault(action => ShortcutChord.Parse(Value(action.Id)) == chord)?.Id;
    }

    /// <summary>
    /// Validates and saves a recorded shortcut; an empty value removes the action's shortcut.
    /// </summary>
    /// <param name="chord">The recorded chord, or null to remove the shortcut.</param>
    /// <param name="id">The action identifier.</param>
    /// <param name="actions">The catalog actions checked for collisions.</param>
    /// <returns>An error message, or null when saved.</returns>
    internal static string? Save(ShortcutChord? chord, string id, IReadOnlyList<DesktopAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (chord is { } value)
        {
            bool control = value.Modifiers.HasFlag(VirtualKeyModifiers.Control);
            bool alt = value.Modifiers.HasFlag(VirtualKeyModifiers.Menu);
            if (!value.Modifiers.HasFlag(VirtualKeyModifiers.Shift) || !(control || alt))
            {
                return "Use Shift with Ctrl or Alt so terminal keys stay available.";
            }

            if (Fixed.Values.Any(text => ShortcutChord.Parse(text) == value))
            {
                return "That shortcut belongs to a standard Weft command.";
            }

            if (actions.FirstOrDefault(action => action.Id != id
                && ShortcutChord.Parse(Value(action.Id)) == value) is { } conflict)
            {
                return "That shortcut is already used by " + conflict.Label.TrimEnd('…') + ".";
            }
        }

        string stored = chord?.ToString() ?? string.Empty;
        PreferencesStore.Update(preferences =>
        {
            var shortcuts = new Dictionary<string, string>(preferences.Shortcuts, StringComparer.Ordinal)
            {
                [id] = stored
            };
            return preferences with { Shortcuts = shortcuts };
        });
        return null;
    }

    /// <summary>
    /// Removes every custom shortcut so the defaults apply again.
    /// </summary>
    internal static void Restore()
    {
        PreferencesStore.Update(preferences => preferences with
        {
            Shortcuts = new Dictionary<string, string>(StringComparer.Ordinal)
        });
    }
}
