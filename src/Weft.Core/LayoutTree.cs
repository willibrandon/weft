namespace Weft.Core;

/// <summary>
/// A tiled layout of blocks as a tree of splits, with tmux's stable resize semantics.
/// </summary>
/// <param name="options">The sizing rules.</param>
public sealed class LayoutTree(LayoutOptions options)
{
    private LayoutCell? _root;

    /// <summary>
    /// Gets the sizing rules.
    /// </summary>
    public LayoutOptions Options { get; } = options;

    /// <summary>
    /// Gets the root cell, or null when the tree holds no blocks.
    /// </summary>
    public LayoutCell? Root => _root;

    /// <summary>
    /// Gets whether the tree holds no blocks.
    /// </summary>
    public bool IsEmpty => _root is null;

    /// <summary>
    /// Gets the width of the whole layout, which may exceed the last fitted width when blocks cannot shrink.
    /// </summary>
    public int Width => _root?.Width ?? 0;

    /// <summary>
    /// Gets the height of the whole layout, which may exceed the last fitted height when blocks cannot shrink.
    /// </summary>
    public int Height => _root?.Height ?? 0;

    /// <summary>
    /// Gets the blocks in layout order, left to right and top to bottom.
    /// </summary>
    public IReadOnlyList<BlockId> Blocks
    {
        get
        {
            return Leaves().Select(leaf => leaf.Block).OfType<BlockId>().ToList();
        }
    }

    /// <summary>
    /// Replaces the whole tree with a single block filling the given size.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="width">The width in columns.</param>
    /// <param name="height">The height in rows.</param>
    public void Initialize(BlockId block, int width, int height)
    {
        var leaf = LayoutCell.CreateLeaf(block);
        leaf.Width = Math.Max(width, Options.MinimumWidth);
        leaf.Height = Math.Max(height, Options.MinimumHeight);
        _root = leaf;
    }

    /// <summary>
    /// Finds the leaf holding a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The leaf, or null when the block is not tiled.</returns>
    public LayoutCell? Find(BlockId block) => Leaves().Find(leaf => leaf.Block == block);

    /// <summary>
    /// Resizes the whole layout to a new size, spreading the change across cells and never
    /// shrinking a block below its minimum.
    /// </summary>
    /// <param name="width">The target width.</param>
    /// <param name="height">The target height.</param>
    public void Fit(int width, int height)
    {
        if (_root is null)
        {
            return;
        }

        int xchange = width - _root.Width;
        if (xchange < 0)
        {
            int limit = ResizeCheck(_root, SplitOrientation.LeftRight);
            if (-xchange > limit)
            {
                xchange = -limit;
            }
        }

        if (xchange != 0)
        {
            ResizeAdjust(_root, SplitOrientation.LeftRight, xchange);
        }

        int ychange = height - _root.Height;
        if (ychange < 0)
        {
            int limit = ResizeCheck(_root, SplitOrientation.TopBottom);
            if (-ychange > limit)
            {
                ychange = -limit;
            }
        }

        if (ychange != 0)
        {
            ResizeAdjust(_root, SplitOrientation.TopBottom, ychange);
        }

        FixOffsets(_root);
    }

    /// <summary>
    /// Splits the leaf holding a block, giving part of its space to a new block.
    /// </summary>
    /// <param name="target">The block to split.</param>
    /// <param name="newBlock">The block to place in the new cell.</param>
    /// <param name="orientation">The split orientation.</param>
    /// <param name="size">The size of the new cell along the orientation, or null for half.</param>
    /// <param name="before">Whether the new cell goes before the target instead of after it.</param>
    /// <returns>The new leaf.</returns>
    /// <exception cref="LayoutException">The target is not tiled or has no room to split.</exception>
    public LayoutCell Split(BlockId target, BlockId newBlock, SplitOrientation orientation, int? size = null, bool before = false)
    {
        LayoutCell cell = Find(target) ?? throw new LayoutException($"Block {target} is not in the layout.");
        int total = cell.Size(orientation);
        int available = total - Options.Spacing;
        int minimum = Options.Minimum(orientation);
        if (available < 2 * minimum)
        {
            throw new LayoutException($"Block {target} has no room to split.");
        }

        int newSize = Math.Clamp(size ?? available / 2, minimum, available - minimum);
        int remaining = available - newSize;
        var leaf = LayoutCell.CreateLeaf(newBlock);

        if (cell.Parent is { } parent && parent.Orientation == orientation)
        {
            int index = cell.IndexInParent();
            cell.SetSize(orientation, remaining);
            leaf.CopyBounds(cell);
            leaf.SetSize(orientation, newSize);
            parent.InsertChild(before ? index : index + 1, leaf);
            FixOffsets(parent);
            return leaf;
        }

        var split = LayoutCell.CreateSplit(orientation);
        split.CopyBounds(cell);
        if (cell.Parent is { } owner)
        {
            owner.ReplaceChild(cell, split);
        }
        else
        {
            _root = split;
        }

        cell.SetSize(orientation, remaining);
        leaf.CopyBounds(split);
        leaf.SetSize(orientation, newSize);
        split.AddChild(before ? leaf : cell);
        split.AddChild(before ? cell : leaf);
        FixOffsets(split);
        return leaf;
    }

    /// <summary>
    /// Removes a block, handing its space to the previous sibling or, for a first child, the next.
    /// </summary>
    /// <param name="block">The block to remove.</param>
    /// <returns>Whether the block was in the layout.</returns>
    public bool Remove(BlockId block)
    {
        LayoutCell? cell = Find(block);
        if (cell is null)
        {
            return false;
        }

        if (cell.Parent is not { } parent)
        {
            _root = null;
            return true;
        }

        SplitOrientation orientation = parent.Orientation!.Value;
        int freed = cell.Size(orientation) + Options.Spacing;
        int index = parent.RemoveChild(cell);
        LayoutCell recipient = parent.Children[index > 0 ? index - 1 : 0];
        ResizeAdjust(recipient, orientation, freed);

        if (parent.Children.Count == 1)
        {
            LayoutCell only = parent.Children[0];
            parent.RemoveChild(only);
            only.X = parent.X;
            only.Y = parent.Y;
            if (parent.Parent is { } grandparent)
            {
                grandparent.ReplaceChild(parent, only);
            }
            else
            {
                _root = only;
            }
        }

        FixOffsets(_root!);
        return true;
    }

    /// <summary>
    /// Moves one edge of a block by an amount, taking or giving space to the neighbour on that side.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="direction">The edge to move: right or down grow toward a following sibling, left or up shrink toward it.</param>
    /// <param name="amount">The number of cells.</param>
    /// <returns>Whether any change was possible.</returns>
    public bool Resize(BlockId block, LayoutDirection direction, int amount)
    {
        LayoutCell? cell = Find(block);
        if (cell is null || amount <= 0)
        {
            return false;
        }

        SplitOrientation orientation = direction is LayoutDirection.Left or LayoutDirection.Right
            ? SplitOrientation.LeftRight
            : SplitOrientation.TopBottom;
        while (cell.Parent is { } ancestor && ancestor.Orientation != orientation)
        {
            cell = ancestor;
        }

        if (cell.Parent is not { } parent)
        {
            return false;
        }

        int index = cell.IndexInParent();
        bool hasNext = index < parent.Children.Count - 1;
        bool grow = direction is LayoutDirection.Right or LayoutDirection.Down ? hasNext : !hasNext;
        LayoutCell other = hasNext ? parent.Children[index + 1] : parent.Children[index - 1];
        int size = Math.Min(amount, ResizeCheck(grow ? other : cell, orientation));
        if (size <= 0)
        {
            return false;
        }

        ResizeAdjust(cell, orientation, grow ? size : -size);
        ResizeAdjust(other, orientation, grow ? -size : size);
        FixOffsets(_root!);
        return true;
    }

    /// <summary>
    /// Rebuilds the tree into a preset arrangement of the given blocks.
    /// </summary>
    /// <param name="preset">The preset.</param>
    /// <param name="blocks">The blocks in order.</param>
    /// <param name="width">The target width.</param>
    /// <param name="height">The target height.</param>
    /// <param name="mainPercent">The share of the main block in the main presets.</param>
    public void ApplyPreset(LayoutPreset preset, IReadOnlyList<BlockId> blocks, int width, int height, int mainPercent = 50)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Count == 0)
        {
            _root = null;
            return;
        }

        if (blocks.Count == 1)
        {
            Initialize(blocks[0], width, height);
            return;
        }

        _root = preset switch
        {
            LayoutPreset.EvenHorizontal => BuildEven(SplitOrientation.LeftRight, blocks, width, height),
            LayoutPreset.EvenVertical => BuildEven(SplitOrientation.TopBottom, blocks, width, height),
            LayoutPreset.MainVertical => BuildMain(SplitOrientation.LeftRight, blocks, width, height, mainPercent),
            LayoutPreset.MainHorizontal => BuildMain(SplitOrientation.TopBottom, blocks, width, height, mainPercent),
            LayoutPreset.Tiled => BuildTiled(blocks, width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
        FixOffsets(_root);
    }

    /// <summary>
    /// Exchanges the blocks held by two leaves, keeping both cells where they are.
    /// </summary>
    /// <param name="first">The first block.</param>
    /// <param name="second">The second block.</param>
    /// <returns>Whether both blocks were tiled.</returns>
    public bool Swap(BlockId first, BlockId second)
    {
        LayoutCell? a = Find(first);
        LayoutCell? b = Find(second);
        if (a is null || b is null)
        {
            return false;
        }

        (a.Block, b.Block) = (b.Block, a.Block);
        return true;
    }

    /// <summary>
    /// Finds the tiled block adjacent to another in a direction, preferring the largest shared edge.
    /// </summary>
    /// <param name="from">The starting block.</param>
    /// <param name="direction">The direction to look.</param>
    /// <returns>The neighbouring block, or null when there is none.</returns>
    public BlockId? FindNeighbor(BlockId from, LayoutDirection direction)
    {
        LayoutCell? origin = Find(from);
        if (origin is null)
        {
            return null;
        }

        LayoutRect a = origin.Bounds;
        BlockId? best = null;
        int bestOverlap = 0;
        int bestStart = int.MaxValue;
        foreach (LayoutCell leaf in Leaves().Where(leaf => leaf != origin && leaf.Block is not null))
        {
            BlockId candidate = leaf.Block!.Value;
            LayoutRect b = leaf.Bounds;
            bool adjacent = direction switch
            {
                LayoutDirection.Left => b.Right + Options.Spacing == a.X,
                LayoutDirection.Right => b.X == a.Right + Options.Spacing,
                LayoutDirection.Up => b.Bottom + Options.Spacing == a.Y,
                LayoutDirection.Down => b.Y == a.Bottom + Options.Spacing,
                _ => false
            };
            if (!adjacent)
            {
                continue;
            }

            bool horizontal = direction is LayoutDirection.Left or LayoutDirection.Right;
            int overlap = horizontal
                ? Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y)
                : Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
            int start = horizontal ? b.Y : b.X;
            if (overlap > bestOverlap || (overlap == bestOverlap && overlap > 0 && start < bestStart))
            {
                best = candidate;
                bestOverlap = overlap;
                bestStart = start;
            }
        }

        return best;
    }

    /// <summary>
    /// Computes the placement of every tiled block.
    /// </summary>
    /// <returns>The placements in layout order.</returns>
    public IReadOnlyList<BlockGeometry> ToGeometry()
    {
        return Leaves().Where(leaf => leaf.Block is not null).Select(leaf => new BlockGeometry(leaf.Block!.Value, leaf.Bounds)).ToList();
    }

    /// <summary>
    /// Serializes the tree to its checksummed string form.
    /// </summary>
    /// <returns>The layout string, or an empty string for an empty tree.</returns>
    public string Serialize() => _root is null ? string.Empty : LayoutSerializer.Serialize(_root);

    /// <summary>
    /// Replaces the tree with a parsed layout, assigning blocks to leaves in order when the
    /// leaf ids do not name exactly the given blocks.
    /// </summary>
    /// <param name="root">The parsed root.</param>
    /// <param name="blocks">The blocks to place.</param>
    /// <param name="width">The width to fit to.</param>
    /// <param name="height">The height to fit to.</param>
    /// <returns>Whether the leaf count matched the block count.</returns>
    public bool TryApply(LayoutCell root, IReadOnlyList<BlockId> blocks, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(blocks);
        List<LayoutCell> leaves = [];
        CollectLeaves(root, leaves);
        if (leaves.Count != blocks.Count)
        {
            return false;
        }

        HashSet<BlockId> expected = [.. blocks];
        bool idsMatch = leaves.TrueForAll(leaf => leaf.Block is { } id && expected.Contains(id))
            && leaves.Select(leaf => leaf.Block).Distinct().Count() == leaves.Count;
        if (!idsMatch)
        {
            for (int i = 0; i < leaves.Count; i++)
            {
                leaves[i].Block = blocks[i];
            }
        }

        _root = root;
        Fit(width, height);
        return true;
    }

    private List<LayoutCell> Leaves()
    {
        List<LayoutCell> leaves = [];
        if (_root is not null)
        {
            CollectLeaves(_root, leaves);
        }

        return leaves;
    }

    private static void CollectLeaves(LayoutCell cell, List<LayoutCell> leaves)
    {
        if (cell.IsLeaf)
        {
            leaves.Add(cell);
            return;
        }

        foreach (LayoutCell child in cell.Children)
        {
            CollectLeaves(child, leaves);
        }
    }

    private int ResizeCheck(LayoutCell cell, SplitOrientation orientation)
    {
        if (cell.IsLeaf)
        {
            return Math.Max(0, cell.Size(orientation) - Options.Minimum(orientation));
        }

        if (cell.Orientation == orientation)
        {
            int sum = 0;
            foreach (LayoutCell child in cell.Children)
            {
                sum += ResizeCheck(child, orientation);
            }

            return sum;
        }

        int minimum = int.MaxValue;
        foreach (LayoutCell child in cell.Children)
        {
            minimum = Math.Min(minimum, ResizeCheck(child, orientation));
        }

        return minimum == int.MaxValue ? 0 : minimum;
    }

    private void ResizeAdjust(LayoutCell cell, SplitOrientation orientation, int change)
    {
        cell.SetSize(orientation, cell.Size(orientation) + change);
        if (cell.IsLeaf)
        {
            return;
        }

        if (cell.Orientation != orientation)
        {
            foreach (LayoutCell child in cell.Children)
            {
                ResizeAdjust(child, orientation, change);
            }

            return;
        }

        while (change != 0)
        {
            bool progressed = false;
            foreach (LayoutCell child in cell.Children)
            {
                if (change == 0)
                {
                    break;
                }

                if (change > 0)
                {
                    ResizeAdjust(child, orientation, 1);
                    change--;
                    progressed = true;
                }
                else if (ResizeCheck(child, orientation) > 0)
                {
                    ResizeAdjust(child, orientation, -1);
                    change++;
                    progressed = true;
                }
            }

            if (!progressed)
            {
                break;
            }
        }
    }

    private void FixOffsets(LayoutCell cell)
    {
        if (cell.IsLeaf)
        {
            return;
        }

        int offset = cell.Orientation == SplitOrientation.LeftRight ? cell.X : cell.Y;
        foreach (LayoutCell child in cell.Children)
        {
            if (cell.Orientation == SplitOrientation.LeftRight)
            {
                child.X = offset;
                child.Y = cell.Y;
                offset += child.Width + Options.Spacing;
            }
            else
            {
                child.Y = offset;
                child.X = cell.X;
                offset += child.Height + Options.Spacing;
            }

            FixOffsets(child);
        }
    }

    private void Spread(LayoutCell split)
    {
        SplitOrientation orientation = split.Orientation!.Value;
        int count = split.Children.Count;
        int total = split.Size(orientation) - (count - 1) * Options.Spacing;
        int each = total / count;
        int extra = total - each * count;
        for (int i = 0; i < count; i++)
        {
            LayoutCell child = split.Children[i];
            child.SetSize(orientation, each + (i < extra ? 1 : 0));
            child.SetSize(Cross(orientation), split.Size(Cross(orientation)));
        }
    }

    private static SplitOrientation Cross(SplitOrientation orientation) =>
        orientation == SplitOrientation.LeftRight ? SplitOrientation.TopBottom : SplitOrientation.LeftRight;

    private int RequiredSize(SplitOrientation orientation, int count) =>
        count * Options.Minimum(orientation) + (count - 1) * Options.Spacing;

    private LayoutCell BuildEven(SplitOrientation orientation, IReadOnlyList<BlockId> blocks, int width, int height)
    {
        var split = LayoutCell.CreateSplit(orientation);
        split.Width = Math.Max(width, orientation == SplitOrientation.LeftRight ? RequiredSize(orientation, blocks.Count) : Options.MinimumWidth);
        split.Height = Math.Max(height, orientation == SplitOrientation.TopBottom ? RequiredSize(orientation, blocks.Count) : Options.MinimumHeight);
        foreach (BlockId block in blocks)
        {
            split.AddChild(LayoutCell.CreateLeaf(block));
        }

        Spread(split);
        return split;
    }

    private LayoutCell BuildMain(SplitOrientation orientation, IReadOnlyList<BlockId> blocks, int width, int height, int mainPercent)
    {
        SplitOrientation cross = Cross(orientation);
        int others = blocks.Count - 1;
        var split = LayoutCell.CreateSplit(orientation);
        int alongMinimum = 2 * Options.Minimum(orientation) + Options.Spacing;
        int crossMinimum = RequiredSize(cross, others);
        split.SetSize(orientation, Math.Max(orientation == SplitOrientation.LeftRight ? width : height, alongMinimum));
        split.SetSize(cross, Math.Max(cross == SplitOrientation.LeftRight ? width : height, crossMinimum));

        int along = split.Size(orientation) - Options.Spacing;
        int mainSize = Math.Clamp(along * Math.Clamp(mainPercent, 1, 99) / 100, Options.Minimum(orientation), along - Options.Minimum(orientation));
        var main = LayoutCell.CreateLeaf(blocks[0]);
        main.SetSize(orientation, mainSize);
        main.SetSize(cross, split.Size(cross));
        split.AddChild(main);

        var rest = LayoutCell.CreateSplit(cross);
        rest.SetSize(orientation, along - mainSize);
        rest.SetSize(cross, split.Size(cross));
        for (int i = 1; i < blocks.Count; i++)
        {
            rest.AddChild(LayoutCell.CreateLeaf(blocks[i]));
        }

        split.AddChild(rest);
        Spread(rest);
        return split;
    }

    private LayoutCell BuildTiled(IReadOnlyList<BlockId> blocks, int width, int height)
    {
        int count = blocks.Count;
        int rows = 1;
        int columns = 1;
        while (rows * columns < count)
        {
            rows++;
            if (rows * columns < count)
            {
                columns++;
            }
        }

        var root = LayoutCell.CreateSplit(SplitOrientation.TopBottom);
        root.Width = Math.Max(width, RequiredSize(SplitOrientation.LeftRight, columns));
        root.Height = Math.Max(height, RequiredSize(SplitOrientation.TopBottom, rows));
        int index = 0;
        for (int row = 0; row < rows && index < count; row++)
        {
            int inRow = Math.Min(columns, count - index);
            if (inRow == 1)
            {
                root.AddChild(LayoutCell.CreateLeaf(blocks[index]));
                index++;
                continue;
            }

            var line = LayoutCell.CreateSplit(SplitOrientation.LeftRight);
            for (int column = 0; column < inRow; column++)
            {
                line.AddChild(LayoutCell.CreateLeaf(blocks[index]));
                index++;
            }

            root.AddChild(line);
        }

        Spread(root);
        foreach (LayoutCell child in root.Children)
        {
            if (!child.IsLeaf)
            {
                Spread(child);
            }
        }

        return root;
    }
}
