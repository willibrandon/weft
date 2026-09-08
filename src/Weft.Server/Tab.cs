using Weft.Core;

namespace Weft.Server;

/// <summary>
/// A tab: a layout of tiled blocks plus floating blocks inside a session.
/// </summary>
internal sealed class Tab
{
    /// <summary>
    /// Initializes a tab record.
    /// </summary>
    /// <param name="id">The tab id.</param>
    /// <param name="session">The owning session.</param>
    /// <param name="name">The tab name.</param>
    /// <param name="options">The layout options.</param>
    internal Tab(TabId id, Session session, string name, LayoutOptions options)
    {
        Id = id;
        Session = session;
        Name = name;
        Layout = new LayoutTree(options);
    }

    /// <summary>
    /// Gets the tab id.
    /// </summary>
    internal TabId Id { get; }

    /// <summary>
    /// Gets the owning session.
    /// </summary>
    internal Session Session { get; }

    /// <summary>
    /// Gets or sets the tab name.
    /// </summary>
    internal string Name { get; set; }

    /// <summary>
    /// Gets whether the name was set explicitly rather than following the active block's title.
    /// </summary>
    internal bool NamePinned { get; set; }

    /// <summary>
    /// Gets the tiled layout.
    /// </summary>
    internal LayoutTree Layout { get; }

    /// <summary>
    /// Gets every block in the tab in creation order.
    /// </summary>
    internal List<Block> Blocks { get; } = [];

    /// <summary>
    /// Gets or sets the active block.
    /// </summary>
    internal Block? Active { get; set; }

    /// <summary>
    /// Gets or sets the zoomed block.
    /// </summary>
    internal Block? Zoomed { get; set; }

    /// <summary>
    /// Gets or sets whether typed input is sent to every block.
    /// </summary>
    internal bool Synchronized { get; set; }

    /// <summary>
    /// Gets or sets the preset the tab was last set to, for cycling.
    /// </summary>
    internal LayoutPreset LastPreset { get; set; } = LayoutPreset.Tiled;

    /// <summary>
    /// Finds a block by id.
    /// </summary>
    /// <param name="id">The block id.</param>
    /// <returns>The block, or null.</returns>
    internal Block? Find(BlockId id) => Blocks.Find(block => block.Id == id);

    /// <summary>
    /// Gets the blocks in layout order: tiled first in tree order, then floating in creation order.
    /// </summary>
    /// <returns>The ordered blocks.</returns>
    internal List<Block> Ordered()
    {
        List<Block> ordered = [];
        foreach (BlockId id in Layout.Blocks)
        {
            if (Find(id) is { } block)
            {
                ordered.Add(block);
            }
        }

        ordered.AddRange(Blocks.Where(block => block.Floating));
        return ordered;
    }
}
