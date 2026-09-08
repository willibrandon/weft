namespace Weft.Protocol;

/// <summary>
/// The closed set of error codes a response may carry.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// The line was not a well-formed request.
    /// </summary>
    public const string InvalidRequest = "invalidRequest";

    /// <summary>
    /// The method name is not known to this server.
    /// </summary>
    public const string UnknownMethod = "unknownMethod";

    /// <summary>
    /// The parameters were missing, malformed, or out of range.
    /// </summary>
    public const string InvalidParams = "invalidParams";

    /// <summary>
    /// The target session, tab, block, or client does not exist.
    /// </summary>
    public const string NotFound = "notFound";

    /// <summary>
    /// The operation conflicts with current state, such as a duplicate name.
    /// </summary>
    public const string Conflict = "conflict";

    /// <summary>
    /// The operation is not possible right now, such as splitting a block with no room.
    /// </summary>
    public const string Unavailable = "unavailable";

    /// <summary>
    /// A wait ran out of time.
    /// </summary>
    public const string Timeout = "timeout";

    /// <summary>
    /// The server failed unexpectedly; details are in the server log.
    /// </summary>
    public const string Internal = "internal";
}
