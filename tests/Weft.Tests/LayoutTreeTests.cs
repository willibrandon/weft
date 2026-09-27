using Weft.Core;

namespace Weft.Tests;

/// <summary>
/// Verifies the tiled layout tree: splitting, removing, resizing, fitting, presets, and neighbours.
/// </summary>
[TestClass]
public sealed class LayoutTreeTests
{
    private static readonly BlockId s_one = new(1);
    private static readonly BlockId s_two = new(2);
    private static readonly BlockId s_three = new(3);

    /// <summary>
    /// Verifies a fresh tree places its first block over the whole area.
    /// </summary>
    [TestMethod]
    public void InitializeFillsArea()
    {
        var tree = new LayoutTree(LayoutOptions.Separated);

        tree.Initialize(s_one, 80, 24);

        IReadOnlyList<BlockGeometry> geometry = tree.ToGeometry();
        Assert.HasCount(1, geometry);
        Assert.AreEqual(new LayoutRect(0, 0, 80, 24), geometry[0].Bounds);
    }

    /// <summary>
    /// Verifies a left-right split leaves a one-cell separator and gives the new block the smaller half.
    /// </summary>
    [TestMethod]
    public void SplitLeftRightSharesWidthWithSeparator()
    {
        var tree = new LayoutTree(LayoutOptions.Separated);
        tree.Initialize(s_one, 80, 24);

        tree.Split(s_one, s_two, SplitOrientation.LeftRight);

        IReadOnlyList<BlockGeometry> geometry = tree.ToGeometry();
        Assert.AreEqual(new LayoutRect(0, 0, 40, 24), geometry[0].Bounds);
        Assert.AreEqual(new LayoutRect(41, 0, 39, 24), geometry[1].Bounds);
    }

    /// <summary>
    /// Verifies splitting again in the same orientation inserts a sibling instead of nesting.
    /// </summary>
    [TestMethod]
    public void SplitSameOrientationInsertsSibling()
    {
        var tree = new LayoutTree(LayoutOptions.Framed);
        tree.Initialize(s_one, 90, 30);
        tree.Split(s_one, s_two, SplitOrientation.LeftRight);

        tree.Split(s_two, s_three, SplitOrientation.LeftRight);

        Assert.IsFalse(tree.Root!.IsLeaf);
        Assert.HasCount(3, tree.Root.Children);
        Assert.AreEqual(90, tree.ToGeometry().Sum(g => g.Bounds.Width));
    }

    /// <summary>
    /// Verifies removing a block hands its space to the previous sibling and collapses single-child splits.
    /// </summary>
    [TestMethod]
    public void RemoveGivesSpaceToPreviousSibling()
    {
        var tree = new LayoutTree(LayoutOptions.Separated);
        tree.Initialize(s_one, 80, 24);
        tree.Split(s_one, s_two, SplitOrientation.LeftRight);

        Assert.IsTrue(tree.Remove(s_two));

        Assert.IsTrue(tree.Root!.IsLeaf);
        Assert.AreEqual(new LayoutRect(0, 0, 80, 24), tree.ToGeometry()[0].Bounds);
    }

    /// <summary>
    /// Verifies resizing moves the shared edge and never shrinks a block below its minimum.
    /// </summary>
    [TestMethod]
    public void ResizeMovesSharedEdgeWithinMinimums()
    {
        var tree = new LayoutTree(LayoutOptions.Separated);
        tree.Initialize(s_one, 80, 24);
        tree.Split(s_one, s_two, SplitOrientation.LeftRight);

        Assert.IsTrue(tree.Resize(s_one, LayoutDirection.Right, 10));
        Assert.AreEqual(50, tree.Find(s_one)!.Width);
        Assert.AreEqual(29, tree.Find(s_two)!.Width);

        Assert.IsTrue(tree.Resize(s_one, LayoutDirection.Right, 100));
        Assert.AreEqual(1, tree.Find(s_two)!.Width);
        Assert.IsFalse(tree.Resize(s_one, LayoutDirection.Right, 1));
    }

    /// <summary>
    /// Verifies fitting to a new size spreads the change across siblings and grows past a stuck minimum.
    /// </summary>
    [TestMethod]
    public void FitSpreadsChangeAndHonoursMinimums()
    {
        var tree = new LayoutTree(LayoutOptions.Framed);
        tree.Initialize(s_one, 80, 24);
        tree.Split(s_one, s_two, SplitOrientation.LeftRight);

        tree.Fit(100, 30);
        Assert.AreEqual(100, tree.Width);
        Assert.AreEqual(30, tree.Height);
        Assert.AreEqual(50, tree.Find(s_one)!.Width);
        Assert.AreEqual(50, tree.Find(s_two)!.Width);

        tree.Fit(4, 30);
        Assert.AreEqual(6, tree.Width, "two framed blocks cannot shrink below three columns each");
    }

    /// <summary>
    /// Verifies the tiled preset arranges three blocks in two rows with the last row spanning the width.
    /// </summary>
    [TestMethod]
    public void TiledPresetBuildsGrid()
    {
        var tree = new LayoutTree(LayoutOptions.Separated);

        tree.ApplyPreset(LayoutPreset.Tiled, [s_one, s_two, s_three], 81, 23);

        IReadOnlyList<BlockGeometry> geometry = tree.ToGeometry();
        Assert.HasCount(3, geometry);
        Assert.AreEqual(new LayoutRect(0, 0, 40, 11), geometry[0].Bounds);
        Assert.AreEqual(new LayoutRect(41, 0, 40, 11), geometry[1].Bounds);
        Assert.AreEqual(new LayoutRect(0, 12, 81, 11), geometry[2].Bounds);
    }

    /// <summary>
    /// Verifies the main-vertical preset gives the first block the requested share.
    /// </summary>
    [TestMethod]
    public void MainVerticalPresetHonoursMainShare()
    {
        var tree = new LayoutTree(LayoutOptions.Separated);

        tree.ApplyPreset(LayoutPreset.MainVertical, [s_one, s_two, s_three], 101, 23, mainPercent: 60);

        IReadOnlyList<BlockGeometry> geometry = tree.ToGeometry();
        Assert.AreEqual(new LayoutRect(0, 0, 60, 23), geometry[0].Bounds);
        Assert.AreEqual(61, geometry[1].Bounds.X);
        Assert.AreEqual(40, geometry[1].Bounds.Width);
        Assert.AreEqual(23, geometry[1].Bounds.Height + 1 + geometry[2].Bounds.Height);
    }

    /// <summary>
    /// Verifies neighbour lookup follows shared edges in every direction.
    /// </summary>
    [TestMethod]
    public void FindNeighborFollowsSharedEdges()
    {
        var tree = new LayoutTree(LayoutOptions.Separated);
        tree.ApplyPreset(LayoutPreset.Tiled, [s_one, s_two, s_three], 81, 23);

        Assert.AreEqual(s_two, tree.FindNeighbor(s_one, LayoutDirection.Right));
        Assert.AreEqual(s_one, tree.FindNeighbor(s_two, LayoutDirection.Left));
        Assert.AreEqual(s_three, tree.FindNeighbor(s_one, LayoutDirection.Down));
        Assert.AreEqual(s_one, tree.FindNeighbor(s_three, LayoutDirection.Up));
        Assert.IsNull(tree.FindNeighbor(s_one, LayoutDirection.Up));
    }

    /// <summary>
    /// Verifies a serialized layout parses back with a valid checksum and identical geometry.
    /// </summary>
    [TestMethod]
    public void SerializeRoundTripsThroughParse()
    {
        var tree = new LayoutTree(LayoutOptions.Separated);
        tree.ApplyPreset(LayoutPreset.MainHorizontal, [s_one, s_two, s_three], 80, 24);
        string text = tree.Serialize();

        Assert.IsTrue(LayoutSerializer.TryParse(text, out LayoutCell? parsed));
        Assert.IsNotNull(parsed);
        var applied = new LayoutTree(LayoutOptions.Separated);
        Assert.IsTrue(applied.TryApply(parsed, [s_one, s_two, s_three], 80, 24));

        Assert.AreSequenceEqual(tree.ToGeometry(), applied.ToGeometry());
        Assert.IsFalse(LayoutSerializer.TryParse(text.Replace('0', '1'), out _), "a corrupted string must fail its checksum");
    }

    /// <summary>
    /// Verifies a tmux layout string with matching leaf count applies and one with a different count is refused.
    /// </summary>
    [TestMethod]
    public void TryApplyRefusesMismatchedLeafCount()
    {
        Assert.IsTrue(LayoutSerializer.TryParse("020a,80x24,0,0{40x24,0,0,1,39x24,41,0,2}", out LayoutCell? parsed));
        Assert.IsNotNull(parsed);
        var tree = new LayoutTree(LayoutOptions.Separated);

        Assert.IsFalse(tree.TryApply(parsed, [s_one, s_two, s_three], 80, 24));
        Assert.IsTrue(tree.TryApply(parsed, [s_two, s_three], 80, 24));
        Assert.AreSequenceEqual([s_two, s_three], tree.Blocks);
    }
}
