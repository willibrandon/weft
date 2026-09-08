namespace Weft.Protocol;

/// <summary>
/// An attached client as reported by the server.
/// </summary>
public sealed class ClientInfo
{
    /// <summary>
    /// Gets the client id assigned on attach.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the session the client is attached to.
    /// </summary>
    public required string Session { get; init; }

    /// <summary>
    /// Gets the client's name, such as its host or terminal.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the viewport width in columns.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the viewport height in rows.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets whether the client's input is discarded.
    /// </summary>
    public required bool ReadOnly { get; init; }

    /// <summary>
    /// Gets when the client attached.
    /// </summary>
    public required DateTimeOffset AttachedAt { get; init; }
}
