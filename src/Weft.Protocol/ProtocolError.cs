namespace Weft.Protocol;

/// <summary>
/// The error part of a failed response.
/// </summary>
public sealed class ProtocolError
{
    /// <summary>
    /// Gets the stable error code from <see cref="ErrorCodes"/>.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the human-readable message.
    /// </summary>
    public required string Message { get; init; }
}
