using Weft.Client;
using Windows.System;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Checks Windows shortcut defaults, recording rules, and the keys sent to terminals.
/// </summary>
[TestClass]
public sealed class ShortcutTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Every stored shortcut parses, round-trips, and shows in the Windows style.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public Task DefaultsRoundTripAndDisplay()
    {
        return DesktopApp.RunAsync(() =>
        {
            foreach (string text in WindowsShortcuts.Defaults.Values.Concat(WindowsShortcuts.Fixed.Values))
            {
                ShortcutChord chord = ShortcutChord.Parse(text)
                    ?? throw new AssertFailedException("Unparsable " + text);
                Assert.AreEqual(text, chord.ToString());
            }

            Assert.AreEqual("Ctrl+Shift+T", WindowsShortcuts.Display("newTab"));
            Assert.AreEqual("Alt+Shift+Plus", WindowsShortcuts.Display("splitRight"));
            Assert.AreEqual("Ctrl+,", WindowsShortcuts.Display("settings"));
            Assert.AreEqual(ShortcutChord.Parse("ctrl+plus"), new ShortcutChord(ShortcutChord.Normalize(VirtualKey.Add),
                VirtualKeyModifiers.Control), "The numeric keypad plus key enlarges text too.");
            Assert.IsNull(ShortcutChord.Parse("ctrl+nothing"));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Key presses match app commands and catalog actions, and plain terminal keys match nothing.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public Task PressesMatchCommands()
    {
        return DesktopApp.RunAsync(() =>
        {
            IReadOnlyList<DesktopAction> actions = DesktopActions.All;
            Assert.AreEqual("newTab", WindowsShortcuts.Match(Chord("ctrl+shift+t"), actions));
            Assert.AreEqual("copy", WindowsShortcuts.Match(Chord("ctrl+shift+c"), actions));
            Assert.AreEqual("commands", WindowsShortcuts.Match(Chord("ctrl+shift+p"), actions));
            Assert.IsNull(WindowsShortcuts.Match(new ShortcutChord(VirtualKey.C, VirtualKeyModifiers.Control), actions),
                "Ctrl+C belongs to the terminal.");
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Recording rejects chords terminals need and chords already taken, and accepts free ones.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public Task RecordingRejectsConflicts()
    {
        return DesktopApp.RunAsync(() =>
        {
            IReadOnlyList<DesktopAction> actions = DesktopActions.All;
            try
            {
                Assert.Contains("Use Shift", WindowsShortcuts.Save(
                    new ShortcutChord(VirtualKey.K, VirtualKeyModifiers.Control), "find", actions)!);
                Assert.Contains("standard Weft command",
                    WindowsShortcuts.Save(Chord("ctrl+shift+c"), "find", actions)!);
                Assert.Contains("New Tab", WindowsShortcuts.Save(Chord("ctrl+shift+t"), "find", actions)!);
                Assert.IsNull(WindowsShortcuts.Save(Chord("ctrl+shift+k"), "find", actions));
                Assert.AreEqual("find", WindowsShortcuts.Match(Chord("ctrl+shift+k"), actions));
                Assert.IsNull(WindowsShortcuts.Match(Chord("ctrl+shift+f"), actions),
                    "The old shortcut still ran Find.");
                Assert.IsNull(WindowsShortcuts.Save(null, "find", actions));
                Assert.AreEqual(string.Empty, WindowsShortcuts.Value("find"));
            }
            finally
            {
                WindowsShortcuts.Restore();
            }

            Assert.AreEqual("ctrl+shift+f", WindowsShortcuts.Value("find"));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Keys without text become the named keys the server encodes; text keys are left to text input.
    /// </summary>
    /// <param name="key">The virtual key.</param>
    /// <param name="modifiers">The modifiers.</param>
    /// <param name="expected">The named key, or an empty string for none.</param>
    [TestMethod]
    [DataRow(VirtualKey.Enter, VirtualKeyModifiers.None, "Enter")]
    [DataRow(VirtualKey.Left, VirtualKeyModifiers.Control, "C-Left")]
    [DataRow(VirtualKey.Tab, VirtualKeyModifiers.Shift, "S-Tab")]
    [DataRow(VirtualKey.F5, VirtualKeyModifiers.None, "F5")]
    [DataRow(VirtualKey.C, VirtualKeyModifiers.Control, "C-c")]
    [DataRow(VirtualKey.X, VirtualKeyModifiers.Menu, "M-x")]
    [DataRow(VirtualKey.A, VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "M-A")]
    [DataRow(VirtualKey.Space, VirtualKeyModifiers.Control, "C-@")]
    [DataRow((VirtualKey)0xDB, VirtualKeyModifiers.Control, "C-[")]
    [DataRow(VirtualKey.Number6, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "C-^")]
    [DataRow(VirtualKey.A, VirtualKeyModifiers.None, "")]
    [DataRow(VirtualKey.Number1, VirtualKeyModifiers.Shift, "")]
    public void KeysHaveTerminalNames(VirtualKey key, VirtualKeyModifiers modifiers, string expected)
    {
        Assert.AreEqual(expected, TerminalKeys.Name(key, modifiers, altGraph: false) ?? string.Empty);
        Assert.IsNull(TerminalKeys.Name(key, modifiers | VirtualKeyModifiers.Control, altGraph: true),
            "AltGr produces text through the keyboard layout.");
    }

    private static ShortcutChord Chord(string text)
    {
        return ShortcutChord.Parse(text) ?? throw new AssertFailedException("Unparsable " + text);
    }
}
