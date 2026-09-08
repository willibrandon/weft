using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Weft.Mcp;

/// <summary>
/// A logger that formats each entry with a timestamp and category and hands it to <see cref="StderrLoggerFactory"/>.
/// </summary>
/// <param name="factory">The factory that owns the output.</param>
/// <param name="category">The logger category.</param>
internal sealed class StderrLogger(StderrLoggerFactory factory, string category) : ILogger
{
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= factory.MinimumLevel;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss.fff} {logLevel} {category} {formatter(state, exception)}");
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        factory.Write(line);
    }
}
