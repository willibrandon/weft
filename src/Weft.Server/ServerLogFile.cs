namespace Weft.Server;

/// <summary>
/// One server's log file, open from the moment the server starts until it stops.
/// </summary>
internal sealed class ServerLogFile : IDisposable
{
    private readonly Lock _gate = new();
    private StreamWriter? _writer;

    /// <summary>
    /// Opens a log file for appending.
    /// </summary>
    /// <param name="path">The log file path.</param>
    internal ServerLogFile(string path)
    {
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    /// <summary>
    /// Appends a line, or does nothing once the server has stopped.
    /// </summary>
    /// <param name="line">The line.</param>
    internal void WriteLine(string line)
    {
        lock (_gate)
        {
            _writer?.WriteLine(line);
        }
    }

    /// <summary>
    /// Closes the file; work that outlives the server no longer writes to it.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
