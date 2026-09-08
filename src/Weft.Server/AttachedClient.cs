namespace Weft.Server;

/// <summary>
/// A client viewport registered on a session.
/// </summary>
internal sealed class AttachedClient
{
    /// <summary>
    /// Initializes a client record.
    /// </summary>
    /// <param name="id">The client id.</param>
    /// <param name="session">The session.</param>
    /// <param name="name">The display name.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="readOnly">Whether input is discarded.</param>
    /// <param name="connectionId">The control connection that registered the client.</param>
    internal AttachedClient(string id, Session session, string name, int width, int height, bool readOnly, long connectionId)
    {
        Id = id;
        Session = session;
        Name = name;
        Width = width;
        Height = height;
        ReadOnly = readOnly;
        ConnectionId = connectionId;
        AttachedAt = DateTimeOffset.Now;
        LastActive = AttachedAt;
    }

    /// <summary>
    /// Gets the client id.
    /// </summary>
    internal string Id { get; }

    /// <summary>
    /// Gets the session.
    /// </summary>
    internal Session Session { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    internal string Name { get; }

    /// <summary>
    /// Gets or sets the viewport width.
    /// </summary>
    internal int Width { get; set; }

    /// <summary>
    /// Gets or sets the viewport height.
    /// </summary>
    internal int Height { get; set; }

    /// <summary>
    /// Gets whether input is discarded.
    /// </summary>
    internal bool ReadOnly { get; }

    /// <summary>
    /// Gets the control connection that registered the client.
    /// </summary>
    internal long ConnectionId { get; }

    /// <summary>
    /// Gets when the client attached.
    /// </summary>
    internal DateTimeOffset AttachedAt { get; }

    /// <summary>
    /// Gets or sets when the client last reported activity.
    /// </summary>
    internal DateTimeOffset LastActive { get; set; }
}
