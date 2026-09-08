namespace Weft.Protocol;

/// <summary>
/// Raised by request handlers to produce an error response with a stable code.
/// </summary>
public sealed class ProtocolException : Exception
{
    /// <summary>
    /// Initializes the exception with the internal error code.
    /// </summary>
    public ProtocolException()
        : this(ErrorCodes.Internal, "The request failed.")
    {
    }

    /// <summary>
    /// Initializes the exception with the internal error code and a message.
    /// </summary>
    /// <param name="message">The message.</param>
    public ProtocolException(string message)
        : this(ErrorCodes.Internal, message)
    {
    }

    /// <summary>
    /// Initializes the exception with the internal error code, a message, and an inner exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = ErrorCodes.Internal;
    }

    /// <summary>
    /// Initializes the exception with a code and a message.
    /// </summary>
    /// <param name="code">The error code from <see cref="ErrorCodes"/>.</param>
    /// <param name="message">The message.</param>
    public ProtocolException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public string Code { get; }
}
