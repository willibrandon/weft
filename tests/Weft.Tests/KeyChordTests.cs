using Weft.Core;

namespace Weft.Tests;

/// <summary>
/// Verifies chord text parsing.
/// </summary>
[TestClass]
public sealed class KeyChordTests
{
    /// <summary>
    /// Verifies direct shortcuts, modifiers, and aliases parse to normalized strokes.
    /// </summary>
    [TestMethod]
    public void ParsesModifiersAndAliases()
    {
        Assert.IsTrue(KeyChord.TryParse("ctrl+shift+h", out KeyChord chord));
        Assert.HasCount(1, chord.Steps);
        Assert.AreEqual(new KeyStroke(KeyModifiers.Control | KeyModifiers.Shift, "h"), chord.Steps[0]);
        Assert.AreEqual("ctrl+shift+h", chord.ToString());

        Assert.IsTrue(KeyChord.TryParse("alt+Enter", out KeyChord single));
        Assert.AreEqual(new KeyStroke(KeyModifiers.Alt, "enter"), single.Steps[0]);
        Assert.IsTrue(KeyChord.TryParse("pgup", out KeyChord page));
        Assert.AreEqual("pageup", page.Steps[0].Key);
        Assert.IsTrue(KeyChord.TryParse("F5", out KeyChord function));
        Assert.AreEqual("f5", function.Steps[0].Key);
    }

    /// <summary>
    /// Verifies two parses of the same text compare equal, so configured overrides replace defaults.
    /// </summary>
    [TestMethod]
    public void EqualChordsCompareByStrokes()
    {
        Assert.IsTrue(KeyChord.TryParse("alt+x", out KeyChord first));
        Assert.IsTrue(KeyChord.TryParse(" alt+x ", out KeyChord second));
        Assert.IsTrue(KeyChord.TryParse("alt+shift+x", out KeyChord shifted));

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreNotEqual(first, shifted);
        Assert.Contains(second, new HashSet<KeyChord> { first });
    }

    /// <summary>
    /// Verifies unknown keys, stray modifiers, and key sequences are rejected.
    /// </summary>
    /// <param name="text">The chord text.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("leader x")]
    [DataRow("ctrl+b x")]
    [DataRow("super+x")]
    [DataRow("ctrl+")]
    [DataRow("%")]
    [DataRow("f13")]
    public void RejectsInvalidChords(string text)
    {
        Assert.IsFalse(KeyChord.TryParse(text, out _));
    }
}
