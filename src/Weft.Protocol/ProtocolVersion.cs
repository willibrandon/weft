namespace Weft.Protocol;

/// <summary>
/// The control protocol version negotiated in the server's hello event.
/// </summary>
public static class ProtocolVersion
{
    /// <summary>
    /// The current protocol version; bumped only for incompatible changes.
    /// </summary>
    public const int Current = 1;
}
