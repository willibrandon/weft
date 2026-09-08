using Weft.Core;

namespace Weft.Tests;

/// <summary>
/// Verifies chord text parsing.
/// </summary>
[TestClass]
public sealed class KeyChordTests
{
    /// <summary>
    /// Verifies modifiers, leader expansion, and aliases parse to normalized strokes.
    /// </summary>
    [TestMethod]
    public void ParsesLeaderModifiersAndAliases()
    {
        Assert.IsTrue(KeyChord.TryParse("ctrl+b", null, out KeyChord leader));
        Assert.IsTrue(KeyChord.TryParse("leader H", leader, out KeyChord chord));

        Assert.HasCount(2, chord.Steps);
        Assert.AreEqual(new KeyStroke(KeyModifiers.Control, "b"), chord.Steps[0]);
        Assert.AreEqual(new KeyStroke(KeyModifiers.Shift, "h"), chord.Steps[1]);
        Assert.AreEqual("ctrl+b shift+h", chord.ToString());

        Assert.IsTrue(KeyChord.TryParse("alt+Enter", null, out KeyChord single));
        Assert.AreEqual(new KeyStroke(KeyModifiers.Alt, "enter"), single.Steps[0]);
        Assert.IsTrue(KeyChord.TryParse("leader pgup", leader, out KeyChord page));
        Assert.AreEqual("pageup", page.Steps[1].Key);
        Assert.IsTrue(KeyChord.TryParse("F5", null, out KeyChord function));
        Assert.AreEqual("f5", function.Steps[0].Key);
    }

    /// <summary>
    /// Verifies two parses of the same text compare equal, so configured overrides replace defaults.
    /// </summary>
    [TestMethod]
    public void EqualChordsCompareByStrokes()
    {
        Assert.IsTrue(KeyChord.TryParse("ctrl+b", null, out KeyChord leader));
        Assert.IsTrue(KeyChord.TryParse("leader x", leader, out KeyChord first));
        Assert.IsTrue(KeyChord.TryParse("leader  x", leader, out KeyChord second));
        Assert.IsTrue(KeyChord.TryParse("leader shift+x", leader, out KeyChord shifted));

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreNotEqual(first, shifted);
        Assert.Contains(second, new HashSet<KeyChord> { first });
    }

    /// <summary>
    /// Verifies unknown keys, stray modifiers, and leader without a leader chord are rejected.
    /// </summary>
    /// <param name="text">The chord text.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("leader x")]
    [DataRow("super+x")]
    [DataRow("ctrl+")]
    [DataRow("%")]
    [DataRow("f13")]
    public void RejectsInvalidChords(string text)
    {
        Assert.IsFalse(KeyChord.TryParse(text, null, out _));
    }
}
