namespace Weft.Core;

/// <summary>
/// How a session chooses its authoritative size from attached clients.
/// </summary>
public enum SizePolicy
{
    /// <summary>
    /// Follow the most recently active client.
    /// </summary>
    Latest,

    /// <summary>
    /// Fit the smallest attached client.
    /// </summary>
    Smallest,

    /// <summary>
    /// Keep a configured size regardless of clients.
    /// </summary>
    Fixed
}
