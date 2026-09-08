using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Session and client lifecycle operations of the registry.
/// </summary>
internal sealed partial class SessionRegistry
{
    /// <summary>
    /// Creates a session with one tab and one block.
    /// </summary>
    /// <param name="name">The name, or null for the next free name.</param>
    /// <param name="cwd">The working directory, or null for the server's.</param>
    /// <param name="command">The first block's command, or null for the default shell.</param>
    /// <param name="width">The initial width, or null for the default.</param>
    /// <param name="height">The initial height, or null for the default.</param>
    /// <param name="sizePolicy">The size policy, or null for the default.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The session.</returns>
    internal async Task<Session> CreateSessionAsync(
        string? name,
        string? cwd,
        IReadOnlyList<string>? command,
        int? width,
        int? height,
        SizePolicy? sizePolicy,
        CancellationToken cancellationToken)
    {
        Session session;
        lock (_gate)
        {
            string resolvedName = string.IsNullOrWhiteSpace(name) ? NextSessionName() : name.Trim();
            if (resolvedName.Contains(':', StringComparison.Ordinal) || resolvedName.Contains('.', StringComparison.Ordinal))
            {
                throw new ProtocolException(ErrorCodes.InvalidParams, "Session names cannot contain ':' or '.'.");
            }

            if (_sessions.Exists(existing => string.Equals(existing.Name, resolvedName, StringComparison.Ordinal)))
            {
                throw new ProtocolException(ErrorCodes.Conflict, "A session named " + resolvedName + " already exists.");
            }

            _nextSession++;
            session = new Session(
                new SessionId(_nextSession),
                resolvedName,
                ResolveDirectory(cwd, _options.HomeDirectory),
                Math.Clamp(width ?? _options.DefaultWidth, 20, 1000),
                Math.Clamp(height ?? _options.DefaultHeight, 5, 500),
                sizePolicy ?? _options.DefaultSizePolicy);
            _sessions.Add(session);
        }

        _events.Publish(ProtocolEvents.SessionCreated, new SessionEventData { Session = ToInfo(session) }, ProtocolJsonContext.Default.SessionEventData);
        try
        {
            await CreateTabAsync(session, null, cwd, command, cancellationToken).ConfigureAwait(false);
        }
        catch (ProtocolException)
        {
            await CloseSessionAsync(session, cancellationToken).ConfigureAwait(false);
            throw;
        }

        return session;
    }

    /// <summary>
    /// Renames a session.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="name">The new name.</param>
    internal void RenameSession(Session session, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed.Contains(':', StringComparison.Ordinal) || trimmed.Contains('.', StringComparison.Ordinal))
        {
            throw new ProtocolException(ErrorCodes.InvalidParams, "Session names must be non-empty and cannot contain ':' or '.'.");
        }

        string previous;
        lock (_gate)
        {
            if (_sessions.Exists(other => other != session && string.Equals(other.Name, trimmed, StringComparison.Ordinal)))
            {
                throw new ProtocolException(ErrorCodes.Conflict, "A session named " + trimmed + " already exists.");
            }

            previous = session.Name;
            session.Name = trimmed;
        }

        _store.Delete(previous);
        Persist(session);
        _events.Publish(ProtocolEvents.SessionRenamed, new SessionEventData { Session = ToInfo(session) }, ProtocolJsonContext.Default.SessionEventData);
    }

    /// <summary>
    /// Closes a session, stopping every block.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Cancels the wait for blocks to stop.</param>
    /// <returns>A task that completes when the session is gone.</returns>
    internal Task CloseSessionAsync(Session session, CancellationToken cancellationToken) =>
        CloseSessionAsync(session, forget: true, cancellationToken);

    private async Task CloseSessionAsync(Session session, bool forget, CancellationToken cancellationToken)
    {
        List<Tab> tabs;
        lock (_gate)
        {
            if (!_sessions.Remove(session))
            {
                return;
            }

            tabs = [.. session.Tabs];
        }

        foreach (Tab tab in tabs)
        {
            await CloseTabAsync(tab, closingSession: true, cancellationToken).ConfigureAwait(false);
        }

        List<AttachedClient> clients;
        lock (_gate)
        {
            clients = [.. session.Clients];
            session.Clients.Clear();
        }

        foreach (AttachedClient client in clients)
        {
            _events.Publish(ProtocolEvents.ClientDetached, new ClientEventData { Client = ToInfo(client) }, ProtocolJsonContext.Default.ClientEventData);
        }

        if (forget)
        {
            _store.Delete(session.Name);
        }

        _events.Publish(ProtocolEvents.SessionClosed, new SessionEventData { Session = ToInfo(session) }, ProtocolJsonContext.Default.SessionEventData);
    }

    /// <summary>
    /// Closes every session for server shutdown, keeping their stored files for resurrection.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for blocks to stop.</param>
    /// <returns>A task that completes when every session is gone.</returns>
    internal async Task CloseAllAsync(CancellationToken cancellationToken)
    {
        foreach (Session session in ListSessions())
        {
            Persist(session);
            await CloseSessionAsync(session, forget: false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Registers a client viewport and applies the session's size policy.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="name">The client display name.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="readOnly">Whether input is discarded.</param>
    /// <param name="connectionId">The control connection.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>The client.</returns>
    internal async Task<AttachedClient> AttachAsync(Session session, string name, int width, int height, bool readOnly, long connectionId, CancellationToken cancellationToken)
    {
        AttachedClient client;
        List<PendingResize> resizes;
        lock (_gate)
        {
            _nextClient++;
            client = new AttachedClient("c" + _nextClient.ToString(System.Globalization.CultureInfo.InvariantCulture), session, name, Math.Max(20, width), Math.Max(5, height), readOnly, connectionId);
            session.Clients.Add(client);
            session.LastActive = DateTimeOffset.Now;
            resizes = ApplySizePolicyUnsafe(session);
        }

        _events.Publish(ProtocolEvents.ClientAttached, new ClientEventData { Client = ToInfo(client) }, ProtocolJsonContext.Default.ClientEventData);
        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
        return client;
    }

    /// <summary>
    /// Releases a client viewport.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when the session has re-laid out.</returns>
    internal async Task DetachAsync(AttachedClient client, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            if (!client.Session.Clients.Remove(client))
            {
                return;
            }

            resizes = ApplySizePolicyUnsafe(client.Session);
        }

        _events.Publish(ProtocolEvents.ClientDetached, new ClientEventData { Client = ToInfo(client) }, ProtocolJsonContext.Default.ClientEventData);
        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases every client registered by a control connection.
    /// </summary>
    /// <param name="connectionId">The connection.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when the clients are gone.</returns>
    internal async Task DetachConnectionAsync(long connectionId, CancellationToken cancellationToken)
    {
        List<AttachedClient> clients = [];
        lock (_gate)
        {
            foreach (Session session in _sessions)
            {
                clients.AddRange(session.Clients.Where(client => client.ConnectionId == connectionId));
            }
        }

        foreach (AttachedClient client in clients)
        {
            await DetachAsync(client, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Marks a client as the most recently active and applies the size policy.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when the session has re-laid out.</returns>
    internal async Task ActivateAsync(AttachedClient client, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            client.LastActive = DateTimeOffset.Now;
            client.Session.LastActive = client.LastActive;
            resizes = ApplySizePolicyUnsafe(client.Session);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a client's viewport size and applies the size policy.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="cancellationToken">Cancels resizes.</param>
    /// <returns>A task that completes when the session has re-laid out.</returns>
    internal async Task SetClientSizeAsync(AttachedClient client, int width, int height, CancellationToken cancellationToken)
    {
        List<PendingResize> resizes;
        lock (_gate)
        {
            client.Width = Math.Max(20, width);
            client.Height = Math.Max(5, height);
            client.LastActive = DateTimeOffset.Now;
            resizes = ApplySizePolicyUnsafe(client.Session);
        }

        await ApplyResizesAsync(resizes, cancellationToken).ConfigureAwait(false);
    }

    private List<PendingResize> ApplySizePolicyUnsafe(Session session)
    {
        int width = session.Width;
        int height = session.Height;
        if (session.Clients.Count > 0)
        {
            switch (session.SizePolicy)
            {
                case SizePolicy.Latest:
                    AttachedClient latest = session.Clients.OrderByDescending(client => client.LastActive).First();
                    width = latest.Width;
                    height = latest.Height;
                    break;
                case SizePolicy.Smallest:
                    width = session.Clients.Min(client => client.Width);
                    height = session.Clients.Min(client => client.Height);
                    break;
                case SizePolicy.Fixed:
                    break;
                default:
                    break;
            }
        }

        if (width == session.Width && height == session.Height)
        {
            return [];
        }

        session.Width = width;
        session.Height = height;
        List<PendingResize> resizes = [];
        foreach (Tab tab in session.Tabs)
        {
            resizes.AddRange(RelayoutUnsafe(tab));
        }

        _events.Publish(ProtocolEvents.SessionResized, new SessionEventData { Session = ToInfoUnsafe(session) }, ProtocolJsonContext.Default.SessionEventData);
        return resizes;
    }

    private static string ResolveDirectory(string? requested, string fallback)
    {
        if (!string.IsNullOrEmpty(requested) && Directory.Exists(requested))
        {
            return Path.GetFullPath(requested);
        }

        return fallback;
    }

    private void Persist(Session session)
    {
        StoredSession stored;
        lock (_gate)
        {
            if (!_sessions.Contains(session))
            {
                return;
            }

            stored = ToStoredUnsafe(session);
        }

        try
        {
            _store.Save(stored);
        }
        catch (IOException exception)
        {
            ServerLog.Error("Could not persist session " + session.Name, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ServerLog.Error("Could not persist session " + session.Name, exception);
        }
    }

    private static StoredSession ToStoredUnsafe(Session session)
    {
        List<StoredTab> tabs = [];
        foreach (Tab tab in session.Tabs)
        {
            tabs.Add(new StoredTab
            {
                Name = tab.Name,
                NamePinned = tab.NamePinned,
                Layout = tab.Layout.Serialize(),
                ActiveBlock = tab.Active?.Id.Value,
                Blocks = tab.Blocks.Select(block => new StoredBlock
                {
                    Id = block.Id.Value,
                    Command = block.Command,
                    Args = block.Arguments,
                    Cwd = block.Cwd,
                    Title = block.PinnedTitle,
                    Floating = block.Floating,
                    X = block.FloatingBounds.X,
                    Y = block.FloatingBounds.Y,
                    Width = block.FloatingBounds.Width,
                    Height = block.FloatingBounds.Height
                }).ToList()
            });
        }

        return new StoredSession
        {
            Name = session.Name,
            Cwd = session.Cwd,
            Width = session.Width,
            Height = session.Height,
            SizePolicy = session.SizePolicy,
            Tabs = tabs,
            ActiveTab = session.ActiveTab is { } active ? session.Tabs.IndexOf(active) : 0,
            SavedAt = DateTimeOffset.Now
        };
    }

    /// <summary>
    /// Lists the names of sessions that are stored but not running.
    /// </summary>
    /// <returns>The names.</returns>
    internal IReadOnlyList<string> StoredSessionNames()
    {
        IReadOnlyList<string> names = _store.ListNames();
        lock (_gate)
        {
            return names.Where(name => !_sessions.Exists(session => string.Equals(session.Name, name, StringComparison.Ordinal))).ToList();
        }
    }

    /// <summary>
    /// Resurrects the session a selector names when it is stored but not running.
    /// </summary>
    /// <param name="selector">The selector.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>A task that completes when the session is running or nothing applies.</returns>
    internal async Task EnsureRunningAsync(TargetSelector selector, CancellationToken cancellationToken)
    {
        if (selector.SessionName is not { } name)
        {
            return;
        }

        lock (_gate)
        {
            if (_sessions.Exists(session => string.Equals(session.Name, name, StringComparison.Ordinal)))
            {
                return;
            }
        }

        await ResurrectAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Recreates a stored session that is not running.
    /// </summary>
    /// <param name="name">The session name.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The session, or null when nothing is stored under the name.</returns>
    internal async Task<Session?> ResurrectAsync(string name, CancellationToken cancellationToken)
    {
        StoredSession? stored = _store.Load(name);
        if (stored is null || stored.Tabs.Count == 0)
        {
            return null;
        }

        Session session;
        lock (_gate)
        {
            if (_sessions.Exists(existing => string.Equals(existing.Name, name, StringComparison.Ordinal)))
            {
                return null;
            }

            _nextSession++;
            session = new Session(new SessionId(_nextSession), name, ResolveDirectory(stored.Cwd, _options.HomeDirectory), stored.Width, stored.Height, stored.SizePolicy);
            _sessions.Add(session);
        }

        _events.Publish(ProtocolEvents.SessionCreated, new SessionEventData { Session = ToInfo(session) }, ProtocolJsonContext.Default.SessionEventData);
        foreach (StoredTab storedTab in stored.Tabs)
        {
            await RestoreTabAsync(session, storedTab, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (stored.ActiveTab >= 0 && stored.ActiveTab < session.Tabs.Count)
            {
                session.ActiveTab = session.Tabs[stored.ActiveTab];
            }
        }

        Persist(session);
        return session;
    }
}
