using System.Globalization;

namespace Weft.Server;

/// <summary>
/// Minimal diagnostic logging to standard error and an optional log file.
/// </summary>
internal static class ServerLog
{
    private static readonly Lock s_gate = new();
    private static StreamWriter? s_file;

    /// <summary>
    /// Directs log lines to a file in addition to standard error.
    /// </summary>
    /// <param name="path">The log file path.</param>
    internal static void UseFile(string path)
    {
        lock (s_gate)
        {
            // Earlier writers are left open on purpose: another server in the same process, as in tests, may still log.
            s_file = new StreamWriter(path, append: true) { AutoFlush = true };
        }
    }

    /// <summary>
    /// Writes a debug line to the log file only.
    /// </summary>
    /// <param name="message">The message.</param>
    internal static void Debug(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss.fff} debug {message}");
        lock (s_gate)
        {
            s_file?.WriteLine(line);
        }
    }

    /// <summary>
    /// Writes an informational line.
    /// </summary>
    /// <param name="message">The message.</param>
    internal static void Info(string message) => Write("info", message);

    /// <summary>
    /// Writes a warning line.
    /// </summary>
    /// <param name="message">The message.</param>
    internal static void Warn(string message) => Write("warn", message);

    /// <summary>
    /// Writes an error line with exception details.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    internal static void Error(string message, Exception exception) => Write("error", message + ": " + exception);

    private static void Write(string level, string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss.fff} {level} {message}");
        lock (s_gate)
        {
            Console.Error.WriteLine(line);
            s_file?.WriteLine(line);
        }
    }
}
