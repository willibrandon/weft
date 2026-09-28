using System.Globalization;

namespace Weft.Server;

/// <summary>
/// Minimal diagnostic logging to standard error and the running server's log file.
/// </summary>
/// <remarks>
/// Each server's work runs in the execution context that opened its file, so servers sharing a process,
/// as tests do, keep separate logs, and a stopped server's file is closed rather than left to the next one.
/// </remarks>
internal static class ServerLog
{
    private static readonly AsyncLocal<ServerLogFile?> s_file = new();

    /// <summary>
    /// Directs log lines from the calling server's work to a file until the returned file is disposed.
    /// </summary>
    /// <param name="path">The log file path.</param>
    /// <returns>The open log file, which the server disposes when it stops.</returns>
    internal static ServerLogFile UseFile(string path)
    {
        var file = new ServerLogFile(path);
        s_file.Value = file;
        return file;
    }

    /// <summary>
    /// Writes a debug line to the log file only.
    /// </summary>
    /// <param name="message">The message.</param>
    internal static void Debug(string message)
    {
        s_file.Value?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:HH:mm:ss.fff} debug {message}"));
    }

    /// <summary>
    /// Writes an informational line.
    /// </summary>
    /// <param name="message">The message.</param>
    internal static void Info(string message)
    {
        Write("info", message);
    }

    /// <summary>
    /// Writes a warning line.
    /// </summary>
    /// <param name="message">The message.</param>
    internal static void Warn(string message)
    {
        Write("warn", message);
    }

    /// <summary>
    /// Writes an error line with exception details.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    internal static void Error(string message, Exception exception)
    {
        Write("error", message + ": " + exception);
    }

    private static void Write(string level, string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:HH:mm:ss.fff} {level} {message}");
        Console.Error.WriteLine(line);
        s_file.Value?.WriteLine(line);
    }
}
