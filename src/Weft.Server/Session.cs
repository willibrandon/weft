using Weft.Core;

namespace Weft.Server;

/// <summary>
/// A session: a named, durable set of tabs with an authoritative size.
/// </summary>
internal sealed class Session
{
    /// <summary>
    /// Initializes a session record.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="name">The session name.</param>
    /// <param name="cwd">The working directory for new blocks.</param>
    /// <param name="width">The initial authoritative width.</param>
    /// <param name="height">The initial authoritative height.</param>
    /// <param name="sizePolicy">The size policy.</param>
    internal Session(SessionId id, string name, string cwd, int width, int height, SizePolicy sizePolicy)
    {
        Id = id;
        Name = name;
        Cwd = cwd;
        Width = width;
        Height = height;
        SizePolicy = sizePolicy;
        CreatedAt = DateTimeOffset.Now;
        LastActive = CreatedAt;
    }

    /// <summary>
    /// Gets the session id.
    /// </summary>
    internal SessionId Id { get; }

    /// <summary>
    /// Gets or sets the session name.
    /// </summary>
    internal string Name { get; set; }

    /// <summary>
    /// Gets or sets the working directory for new blocks.
    /// </summary>
    internal string Cwd { get; set; }

    /// <summary>
    /// Gets when the session was created.
    /// </summary>
    internal DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets or sets when the session was last attached or used.
    /// </summary>
    internal DateTimeOffset LastActive { get; set; }

    /// <summary>
    /// Gets or sets the authoritative width.
    /// </summary>
    internal int Width { get; set; }

    /// <summary>
    /// Gets or sets the authoritative height.
    /// </summary>
    internal int Height { get; set; }

    /// <summary>
    /// Gets or sets the size policy.
    /// </summary>
    internal SizePolicy SizePolicy { get; set; }

    /// <summary>
    /// Gets the tabs in index order.
    /// </summary>
    internal List<Tab> Tabs { get; } = [];

    /// <summary>
    /// Gets or sets the active tab.
    /// </summary>
    internal Tab? ActiveTab { get; set; }

    /// <summary>
    /// Gets the attached clients.
    /// </summary>
    internal List<AttachedClient> Clients { get; } = [];

    /// <summary>
    /// Gets the number of blocks across tabs.
    /// </summary>
    internal int BlockCount => Tabs.Sum(tab => tab.Blocks.Count);

    /// <summary>
    /// Finds a tab by id.
    /// </summary>
    /// <param name="id">The tab id.</param>
    /// <returns>The tab, or null.</returns>
    internal Tab? FindTab(TabId id) => Tabs.Find(tab => tab.Id == id);

    /// <summary>
    /// Finds a block by id across tabs.
    /// </summary>
    /// <param name="id">The block id.</param>
    /// <returns>The block, or null.</returns>
    internal Block? FindBlock(BlockId id)
    {
        foreach (Tab tab in Tabs)
        {
            if (tab.Find(id) is { } block)
            {
                return block;
            }
        }

        return null;
    }
}
