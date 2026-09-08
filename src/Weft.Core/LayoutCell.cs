namespace Weft.Core;

/// <summary>
/// A node in a layout tree: either a leaf holding a block or a split holding children.
/// </summary>
public sealed class LayoutCell
{
    private readonly List<LayoutCell> _children = [];

    private LayoutCell(SplitOrientation? orientation, BlockId? block)
    {
        Orientation = orientation;
        Block = block;
    }

    /// <summary>
    /// Gets the split orientation, or null for a leaf.
    /// </summary>
    public SplitOrientation? Orientation { get; }

    /// <summary>
    /// Gets the block held by a leaf, or null for a split.
    /// </summary>
    public BlockId? Block { get; internal set; }

    /// <summary>
    /// Assigns the block held by a leaf, for callers rebuilding a parsed layout.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <exception cref="InvalidOperationException">The cell is a split.</exception>
    public void AssignBlock(BlockId block)
    {
        if (!IsLeaf)
        {
            throw new InvalidOperationException("Only a leaf holds a block.");
        }

        Block = block;
    }

    /// <summary>
    /// Gets whether this cell is a leaf.
    /// </summary>
    public bool IsLeaf => Orientation is null;

    /// <summary>
    /// Gets the parent split, or null for the root.
    /// </summary>
    public LayoutCell? Parent { get; private set; }

    /// <summary>
    /// Gets the children of a split in layout order; empty for a leaf.
    /// </summary>
    public IReadOnlyList<LayoutCell> Children => _children;

    /// <summary>
    /// Gets the leftmost column of this cell.
    /// </summary>
    public int X { get; internal set; }

    /// <summary>
    /// Gets the topmost row of this cell.
    /// </summary>
    public int Y { get; internal set; }

    /// <summary>
    /// Gets the width of this cell in columns.
    /// </summary>
    public int Width { get; internal set; }

    /// <summary>
    /// Gets the height of this cell in rows.
    /// </summary>
    public int Height { get; internal set; }

    /// <summary>
    /// Gets the cell's rectangle.
    /// </summary>
    public LayoutRect Bounds => new(X, Y, Width, Height);

    /// <summary>
    /// Creates a leaf for a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The leaf.</returns>
    internal static LayoutCell CreateLeaf(BlockId block) => new(null, block);

    /// <summary>
    /// Creates an empty split.
    /// </summary>
    /// <param name="orientation">The split orientation.</param>
    /// <returns>The split.</returns>
    internal static LayoutCell CreateSplit(SplitOrientation orientation) => new(orientation, null);

    /// <summary>
    /// Gets the cell's size along an orientation.
    /// </summary>
    /// <param name="orientation">The orientation.</param>
    /// <returns>The width for left-right, otherwise the height.</returns>
    internal int Size(SplitOrientation orientation) =>
        orientation == SplitOrientation.LeftRight ? Width : Height;

    /// <summary>
    /// Sets the cell's size along an orientation.
    /// </summary>
    /// <param name="orientation">The orientation.</param>
    /// <param name="size">The new size.</param>
    internal void SetSize(SplitOrientation orientation, int size)
    {
        if (orientation == SplitOrientation.LeftRight)
        {
            Width = size;
        }
        else
        {
            Height = size;
        }
    }

    /// <summary>
    /// Copies position and size from another cell.
    /// </summary>
    /// <param name="other">The cell to copy from.</param>
    internal void CopyBounds(LayoutCell other)
    {
        X = other.X;
        Y = other.Y;
        Width = other.Width;
        Height = other.Height;
    }

    /// <summary>
    /// Inserts a child at an index and takes ownership of it.
    /// </summary>
    /// <param name="index">The insertion index.</param>
    /// <param name="child">The child.</param>
    internal void InsertChild(int index, LayoutCell child)
    {
        child.Parent = this;
        _children.Insert(index, child);
    }

    /// <summary>
    /// Appends a child and takes ownership of it.
    /// </summary>
    /// <param name="child">The child.</param>
    internal void AddChild(LayoutCell child) => InsertChild(_children.Count, child);

    /// <summary>
    /// Removes a child and clears its parent.
    /// </summary>
    /// <param name="child">The child.</param>
    /// <returns>The index the child occupied.</returns>
    internal int RemoveChild(LayoutCell child)
    {
        int index = _children.IndexOf(child);
        _children.RemoveAt(index);
        child.Parent = null;
        return index;
    }

    /// <summary>
    /// Replaces a child in place with another cell.
    /// </summary>
    /// <param name="existing">The child to replace.</param>
    /// <param name="replacement">The cell to put in its place.</param>
    internal void ReplaceChild(LayoutCell existing, LayoutCell replacement)
    {
        int index = _children.IndexOf(existing);
        existing.Parent = null;
        replacement.Parent = this;
        _children[index] = replacement;
    }

    /// <summary>
    /// Detaches this cell from its parent without touching sizes.
    /// </summary>
    internal void Detach()
    {
        Parent?.RemoveChild(this);
    }

    /// <summary>
    /// Gets the index of this cell within its parent, or -1 for the root.
    /// </summary>
    /// <returns>The index or -1.</returns>
    internal int IndexInParent() => Parent is null ? -1 : Parent._children.IndexOf(this);
}
