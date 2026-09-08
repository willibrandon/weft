using Weft.Core;

namespace Weft.Tests;

/// <summary>
/// Verifies parsing of <c>session:tab.block</c> targets.
/// </summary>
[TestClass]
public sealed class TargetSelectorTests
{
    /// <summary>
    /// Verifies empty text selects the current target.
    /// </summary>
    [TestMethod]
    public void EmptyIsCurrent()
    {
        Assert.IsTrue(TargetSelector.TryParse(string.Empty, out TargetSelector selector));
        Assert.IsTrue(selector.IsEmpty);
    }

    /// <summary>
    /// Verifies bare ids select by id and bare names select a session.
    /// </summary>
    [TestMethod]
    public void BareTokensResolveByKind()
    {
        Assert.IsTrue(TargetSelector.TryParse("b7", out TargetSelector block));
        Assert.AreEqual(new BlockId(7), block.Block);

        Assert.IsTrue(TargetSelector.TryParse("t2", out TargetSelector tab));
        Assert.AreEqual(new TabId(2), tab.Tab);

        Assert.IsTrue(TargetSelector.TryParse("work", out TargetSelector session));
        Assert.AreEqual("work", session.SessionName);
    }

    /// <summary>
    /// Verifies the full path form parses names, indices, and ids in their positions.
    /// </summary>
    [TestMethod]
    public void PathFormParsesEveryPart()
    {
        Assert.IsTrue(TargetSelector.TryParse("main:2.b9", out TargetSelector selector));

        Assert.AreEqual("main", selector.SessionName);
        Assert.AreEqual(2, selector.TabIndex);
        Assert.IsNull(selector.Tab);
        Assert.AreEqual(new BlockId(9), selector.Block);
        Assert.IsNull(selector.BlockIndex);
    }

    /// <summary>
    /// Verifies partial forms starting with a separator leave the missing parts unset.
    /// </summary>
    [TestMethod]
    public void PartialFormsLeaveMissingPartsUnset()
    {
        Assert.IsTrue(TargetSelector.TryParse(".1", out TargetSelector block));
        Assert.AreEqual(1, block.BlockIndex);
        Assert.IsNull(block.SessionName);

        Assert.IsTrue(TargetSelector.TryParse(":t4", out TargetSelector tab));
        Assert.AreEqual(new TabId(4), tab.Tab);

        Assert.IsTrue(TargetSelector.TryParse("b7:", out TargetSelector named));
        Assert.AreEqual("b7", named.SessionName);
    }

    /// <summary>
    /// Verifies malformed parts are rejected.
    /// </summary>
    /// <param name="text">The malformed target.</param>
    [TestMethod]
    [DataRow("main:x")]
    [DataRow("main:1.y")]
    [DataRow("a.b.c")]
    public void MalformedPartsAreRejected(string text)
    {
        Assert.IsFalse(TargetSelector.TryParse(text, out _));
    }
}
