namespace Weft.Core;

/// <summary>
/// Raised when a layout operation cannot be applied, such as splitting a cell with no room.
/// </summary>
public sealed class LayoutException : Exception
{
    /// <summary>
    /// Initializes the exception with a default message.
    /// </summary>
    public LayoutException()
        : base("The layout operation could not be applied.")
    {
    }

    /// <summary>
    /// Initializes the exception with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    public LayoutException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes the exception with a message and an inner exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public LayoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
