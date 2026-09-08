namespace Weft.Protocol;

/// <summary>
/// Writes newline-delimited messages to a stream, one at a time.
/// </summary>
public sealed class ProtocolWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a writer that owns a stream and disposes it with the writer.
    /// </summary>
    /// <param name="stream">The stream.</param>
    public ProtocolWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    /// <summary>
    /// Writes one message and flushes.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the line has been flushed.</returns>
    public async ValueTask WriteAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        byte[] line = ProtocolCodec.EncodeLine(message);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disposes the stream and the write gate.
    /// </summary>
    public void Dispose()
    {
        _stream.Dispose();
        _gate.Dispose();
    }
}
