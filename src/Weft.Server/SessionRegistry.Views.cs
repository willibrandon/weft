using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Conversion of registry state to protocol records.
/// </summary>
internal sealed partial class SessionRegistry
{
    /// <summary>
    /// Describes a session.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>The record.</returns>
    internal SessionInfo ToInfo(Session session)
    {
        lock (_gate)
        {
            return ToInfoUnsafe(session);
        }
    }

    /// <summary>
    /// Describes a tab.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <returns>The record.</returns>
    internal TabInfo ToInfo(Tab tab)
    {
        lock (_gate)
        {
            return ToInfoUnsafe(tab);
        }
    }

    /// <summary>
    /// Describes a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The record.</returns>
    internal BlockInfo ToInfo(Block block)
    {
        lock (_gate)
        {
            return ToInfoUnsafe(block);
        }
    }

    /// <summary>
    /// Describes a client.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <returns>The record.</returns>
    internal static ClientInfo ToInfo(AttachedClient client) => new()
    {
        Id = client.Id,
        Session = client.Session.Id.ToString(),
        SessionName = client.Session.Name,
        Name = client.Name,
        Width = client.Width,
        Height = client.Height,
        ReadOnly = client.ReadOnly,
        AttachedAt = client.AttachedAt
    };

    /// <summary>
    /// Describes a tab's geometry.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <returns>The record.</returns>
    internal LayoutInfo ToLayoutInfo(Tab tab)
    {
        lock (_gate)
        {
            return ToLayoutInfoUnsafe(tab);
        }
    }

    /// <summary>
    /// Builds the full attach snapshot for a session.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="client">The attached client.</param>
    /// <returns>The snapshot.</returns>
    internal SessionAttachResult ToAttachResult(Session session, AttachedClient client)
    {
        lock (_gate)
        {
            Tab active = session.ActiveTab ?? throw new ProtocolException(ErrorCodes.Unavailable, "The session has no tabs.");
            return new SessionAttachResult
            {
                Client = ToInfo(client),
                Session = ToInfoUnsafe(session),
                Tabs = session.Tabs.Select(ToInfoUnsafe).ToList(),
                Blocks = session.Tabs.SelectMany(tab => tab.Ordered()).Select(ToInfoUnsafe).ToList(),
                Layout = ToLayoutInfoUnsafe(active),
                Seq = _events.Seq
            };
        }
    }

    private static SessionInfo ToInfoUnsafe(Session session) => new()
    {
        Id = session.Id.ToString(),
        Name = session.Name,
        CreatedAt = session.CreatedAt,
        Cwd = session.Cwd,
        Tabs = session.Tabs.Count,
        Blocks = session.BlockCount,
        Clients = session.Clients.Count,
        Width = session.Width,
        Height = session.Height,
        SizePolicy = session.SizePolicy,
        ActiveTab = session.ActiveTab?.Id.ToString()
    };

    private static TabInfo ToInfoUnsafe(Tab tab) => new()
    {
        Id = tab.Id.ToString(),
        Index = tab.Session.Tabs.IndexOf(tab) + 1,
        Name = tab.Name,
        Session = tab.Session.Id.ToString(),
        SessionName = tab.Session.Name,
        Blocks = tab.Blocks.Count,
        ActiveBlock = tab.Active?.Id.ToString(),
        Zoomed = tab.Zoomed?.Id.ToString(),
        Active = tab.Session.ActiveTab == tab,
        Synchronized = tab.Synchronized
    };

    private static BlockInfo ToInfoUnsafe(Block block) => new()
    {
        Id = block.Id.ToString(),
        Index = block.Tab.Ordered().IndexOf(block) + 1,
        Tab = block.Tab.Id.ToString(),
        Session = block.Tab.Session.Id.ToString(),
        SessionName = block.Tab.Session.Name,
        Title = block.DisplayTitle,
        Command = block.Command,
        Args = block.Arguments,
        Cwd = block.Cwd,
        State = block.State,
        ExitCode = block.ExitCode,
        Pid = block.Host?.ProcessId,
        Width = block.Width,
        Height = block.Height,
        Floating = block.Floating,
        Active = block.Tab.Active == block,
        ExcludedFromSync = block.ExcludedFromSync,
        Revision = block.Revision,
        SocketPath = block.SocketPath
    };

    private LayoutInfo ToLayoutInfoUnsafe(Tab tab)
    {
        Session session = tab.Session;
        List<BlockPlacement> tiled = [];
        if (tab.Zoomed is { } zoomed)
        {
            tiled.Add(new BlockPlacement { Id = zoomed.Id.ToString(), X = 0, Y = 0, Width = session.Width, Height = session.Height });
        }
        else
        {
            foreach (BlockGeometry geometry in tab.Layout.ToGeometry())
            {
                tiled.Add(new BlockPlacement
                {
                    Id = geometry.Block.ToString(),
                    X = geometry.Bounds.X,
                    Y = geometry.Bounds.Y,
                    Width = geometry.Bounds.Width,
                    Height = geometry.Bounds.Height
                });
            }
        }

        List<BlockPlacement> floating = [];
        foreach (Block block in tab.Blocks.Where(block => block.Floating))
        {
            floating.Add(new BlockPlacement
            {
                Id = block.Id.ToString(),
                X = block.FloatingBounds.X,
                Y = block.FloatingBounds.Y,
                Width = block.FloatingBounds.Width,
                Height = block.FloatingBounds.Height
            });
        }

        return new LayoutInfo
        {
            Tab = tab.Id.ToString(),
            Width = session.Width,
            Height = session.Height,
            FrameSize = _options.FrameSize,
            Zoomed = tab.Zoomed?.Id.ToString(),
            Tiled = tiled,
            Floating = floating,
            Serialized = tab.Layout.Serialize()
        };
    }
}
