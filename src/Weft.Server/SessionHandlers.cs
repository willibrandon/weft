using Weft.Core;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Handlers for session, client, and tab methods.
/// </summary>
internal static class SessionHandlers
{
    /// <summary>
    /// Registers the handlers.
    /// </summary>
    /// <param name="dispatcher">The dispatcher.</param>
    internal static void Register(RequestDispatcher dispatcher)
    {
        dispatcher.Register(ProtocolMethods.SessionList, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.SessionListResult,
            (context, _, _) => Task.FromResult(new SessionListResult
            {
                Sessions = context.Registry.ListSessions().Select(context.Registry.ToInfo).ToList()
            }));

        dispatcher.Register(ProtocolMethods.SessionCreate, ProtocolJsonContext.Default.SessionCreateParams, ProtocolJsonContext.Default.SessionInfo,
            async (context, parameters, cancellationToken) =>
            {
                Session session = await context.Registry.CreateSessionAsync(parameters.Name, parameters.Cwd, parameters.Command, parameters.Width, parameters.Height, parameters.SizePolicy, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToInfo(session);
            });

        dispatcher.Register(ProtocolMethods.SessionGet, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.SessionInfo,
            async (context, parameters, cancellationToken) => (context.Registry.ToInfo(await Targets.SessionAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false))));

        dispatcher.Register(ProtocolMethods.SessionRename, ProtocolJsonContext.Default.SessionRenameParams, ProtocolJsonContext.Default.SessionInfo,
            async (context, parameters, cancellationToken) =>
            {
                Session session = await Targets.SessionAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                context.Registry.RenameSession(session, parameters.Name);
                return (context.Registry.ToInfo(session));
            });

        dispatcher.Register(ProtocolMethods.SessionClose, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                Session session = await Targets.SessionAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.CloseSessionAsync(session, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.SessionAttach, ProtocolJsonContext.Default.SessionAttachParams, ProtocolJsonContext.Default.SessionAttachResult,
            async (context, parameters, cancellationToken) =>
            {
                Session session = await ResolveOrCreateAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                AttachedClient client = await context.Registry.AttachAsync(session, parameters.Name ?? "client", parameters.Width, parameters.Height, parameters.ReadOnly, context.ConnectionId, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToAttachResult(session, client);
            });

        dispatcher.Register(ProtocolMethods.SessionDetach, ProtocolJsonContext.Default.ClientParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await context.Registry.DetachAsync(context.Registry.ResolveClient(parameters.Client), cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.SessionSetSize, ProtocolJsonContext.Default.SessionSetSizeParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                await context.Registry.SetClientSizeAsync(context.Registry.ResolveClient(parameters.Client), parameters.Width, parameters.Height, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });

        dispatcher.Register(ProtocolMethods.TabList, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.TabListResult,
            async (context, parameters, cancellationToken) =>
            {
                Session session = await Targets.SessionAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                return (new TabListResult { Tabs = session.Tabs.Select(context.Registry.ToInfo).ToList() });
            });

        dispatcher.Register(ProtocolMethods.TabCreate, ProtocolJsonContext.Default.TabCreateParams, ProtocolJsonContext.Default.TabInfo,
            async (context, parameters, cancellationToken) =>
            {
                Session session = await Targets.SessionAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                Tab tab = await context.Registry.CreateTabAsync(session, parameters.Name, parameters.Cwd, parameters.Command, cancellationToken).ConfigureAwait(false);
                return context.Registry.ToInfo(tab);
            });

        dispatcher.Register(ProtocolMethods.TabSelect, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.TabInfo,
            async (context, parameters, cancellationToken) =>
            {
                Tab tab = await Targets.TabAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                context.Registry.SelectTab(tab);
                return (context.Registry.ToInfo(tab));
            });

        dispatcher.Register(ProtocolMethods.TabRename, ProtocolJsonContext.Default.TabRenameParams, ProtocolJsonContext.Default.TabInfo,
            async (context, parameters, cancellationToken) =>
            {
                Tab tab = await Targets.TabAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                context.Registry.RenameTab(tab, parameters.Name);
                return (context.Registry.ToInfo(tab));
            });

        dispatcher.Register(ProtocolMethods.TabClose, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.EmptyResult,
            async (context, parameters, cancellationToken) =>
            {
                Tab tab = await Targets.TabAsync(context.Registry, parameters.Target, cancellationToken).ConfigureAwait(false);
                await context.Registry.CloseTabAsync(tab, cancellationToken).ConfigureAwait(false);
                return EmptyResult.Instance;
            });
    }

    /// <summary>
    /// Resolves a session, resurrecting a stored one or creating the first one when nothing matches.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="target">The target text.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The session.</returns>
    internal static async Task<Session> ResolveOrCreateAsync(SessionRegistry registry, string? target, CancellationToken cancellationToken)
    {
        TargetSelector selector = Targets.Parse(target);
        try
        {
            return registry.ResolveSession(selector);
        }
        catch (ProtocolException exception) when (string.Equals(exception.Code, ErrorCodes.NotFound, StringComparison.Ordinal))
        {
            if (selector.SessionName is { } name)
            {
                return await registry.ResurrectAsync(name, cancellationToken).ConfigureAwait(false)
                    ?? await registry.CreateSessionAsync(name, null, null, null, null, null, cancellationToken).ConfigureAwait(false);
            }

            if (selector.IsEmpty)
            {
                foreach (string stored in registry.StoredSessionNames())
                {
                    if (await registry.ResurrectAsync(stored, cancellationToken).ConfigureAwait(false) is { } resurrected)
                    {
                        return resurrected;
                    }
                }

                return await registry.CreateSessionAsync(null, null, null, null, null, null, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }
}
