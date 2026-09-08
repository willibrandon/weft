namespace Weft.Client;

/// <summary>
/// Keeps a bounded ring of diagnostic lines from the client, readable after a failure.
/// </summary>
public static class ClientLog
{
    private const int Capacity = 256;
    private static readonly Lock s_gate = new();
    private static readonly Queue<string> s_lines = new();

    /// <summary>
    /// Records a debug line.
    /// </summary>
    /// <param name="message">The message.</param>
    public static void Debug(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (s_gate)
        {
            if (s_lines.Count == Capacity)
            {
                s_lines.Dequeue();
            }

            s_lines.Enqueue(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}"));
        }
    }

    /// <summary>
    /// Gets the recorded lines, oldest first.
    /// </summary>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Snapshot()
    {
        lock (s_gate)
        {
            return [.. s_lines];
        }
    }
}
