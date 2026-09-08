using System.Buffers;
using System.IO.Pipelines;

namespace Weft.Protocol;

/// <summary>
/// Reads newline-delimited messages from a stream.
/// </summary>
public sealed class ProtocolReader : IAsyncDisposable
{
    private readonly PipeReader _reader;

    /// <summary>
    /// Initializes a reader over a stream, which the reader does not own.
    /// </summary>
    /// <param name="stream">The stream.</param>
    public ProtocolReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
    }

    /// <summary>
    /// Reads the next message.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The message, or null when the stream ended cleanly between messages.</returns>
    /// <exception cref="ProtocolException">A line was malformed or too long.</exception>
    public async ValueTask<ProtocolMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            ReadResult result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            SequencePosition? newline = buffer.PositionOf((byte)'\n');
            if (newline is { } position)
            {
                ReadOnlySequence<byte> line = buffer.Slice(0, position);
                ProtocolMessage message = Decode(line);
                _reader.AdvanceTo(buffer.GetPosition(1, position));
                return message;
            }

            if (buffer.Length > ProtocolCodec.MaximumLineBytes)
            {
                _reader.AdvanceTo(buffer.End);
                throw new ProtocolException(ErrorCodes.InvalidRequest, "The line exceeds the maximum length.");
            }

            if (result.IsCompleted || result.IsCanceled)
            {
                _reader.AdvanceTo(buffer.End);
                if (buffer.Length > 0 && !buffer.IsSingleSegment || (buffer.IsSingleSegment && buffer.First.Span.Trim((byte)' ').Length > 0))
                {
                    throw new ProtocolException(ErrorCodes.InvalidRequest, "The stream ended in the middle of a line.");
                }

                return null;
            }

            _reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>
    /// Completes the underlying pipe without closing the stream.
    /// </summary>
    /// <returns>A task that completes when the reader is released.</returns>
    public async ValueTask DisposeAsync()
    {
        await _reader.CompleteAsync().ConfigureAwait(false);
    }

    private static ProtocolMessage Decode(ReadOnlySequence<byte> line)
    {
        if (line.IsSingleSegment)
        {
            return ProtocolCodec.DecodeLine(TrimCarriageReturn(line.First.Span));
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent((int)line.Length);
        try
        {
            line.CopyTo(rented);
            return ProtocolCodec.DecodeLine(TrimCarriageReturn(rented.AsSpan(0, (int)line.Length)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static ReadOnlySpan<byte> TrimCarriageReturn(ReadOnlySpan<byte> line) =>
        line.Length > 0 && line[^1] == (byte)'\r' ? line[..^1] : line;
}
