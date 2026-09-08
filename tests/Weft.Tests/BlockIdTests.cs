using Weft.Core;

namespace Weft.Tests;

/// <summary>
/// Verifies parsing and formatting of block identifiers.
/// </summary>
[TestClass]
public sealed class BlockIdTests
{
    /// <summary>
    /// Verifies a prefixed positive integer parses to the same value.
    /// </summary>
    [TestMethod]
    public void TryParseAcceptsPrefixedPositiveValue()
    {
        Assert.IsTrue(BlockId.TryParse("b12", out BlockId id));
        Assert.AreEqual(12, id.Value);
    }

    /// <summary>
    /// Verifies text without the prefix, with the wrong prefix, or non-positive is rejected.
    /// </summary>
    /// <param name="text">The text that must be rejected.</param>
    [TestMethod]
    [DataRow("12")]
    [DataRow("t12")]
    [DataRow("b0")]
    [DataRow("b-1")]
    [DataRow("b")]
    [DataRow("")]
    public void TryParseRejectsInvalidText(string text)
    {
        Assert.IsFalse(BlockId.TryParse(text, out _));
    }

    /// <summary>
    /// Verifies formatting round-trips through parsing.
    /// </summary>
    [TestMethod]
    public void ToStringRoundTrips()
    {
        var id = new BlockId(7);

        Assert.AreEqual("b7", id.ToString());
        Assert.IsTrue(BlockId.TryParse(id.ToString(), out BlockId parsed));
        Assert.AreEqual(id, parsed);
    }
}
