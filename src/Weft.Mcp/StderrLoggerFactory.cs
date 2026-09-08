using Microsoft.Extensions.Logging;

namespace Weft.Mcp;

/// <summary>
/// A logger factory that writes to standard error, the channel the protocol reserves for a stdio server's logs.
/// </summary>
/// <param name="minimumLevel">The lowest level that is written.</param>
internal sealed class StderrLoggerFactory(LogLevel minimumLevel) : ILoggerFactory
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Gets the lowest level that is written.
    /// </summary>
    internal LogLevel MinimumLevel => minimumLevel;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new StderrLogger(this, categoryName);

    /// <summary>
    /// Ignored; this factory only ever writes to standard error.
    /// </summary>
    /// <param name="provider">The provider, which is not used.</param>
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// Writes one line.
    /// </summary>
    /// <param name="line">The line.</param>
    internal void Write(string line)
    {
        lock (_gate)
        {
            Console.Error.WriteLine(line);
        }
    }
}
