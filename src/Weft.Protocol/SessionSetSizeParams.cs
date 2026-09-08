namespace Weft.Protocol;

/// <summary>
/// Parameters for session.setSize.
/// </summary>
public sealed class SessionSetSizeParams
{
    /// <summary>
    /// Gets the client id from session.attach.
    /// </summary>
    public required string Client { get; init; }

    /// <summary>
    /// Gets the viewport width in columns.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the viewport height in rows.
    /// </summary>
    public required int Height { get; init; }
}
