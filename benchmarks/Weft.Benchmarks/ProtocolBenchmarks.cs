using BenchmarkDotNet.Attributes;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Benchmarks;

/// <summary>
/// Measures control protocol encoding and decoding.
/// </summary>
[MemoryDiagnoser]
public class ProtocolBenchmarks
{
    private readonly BlockSplitParams _split = new() { Target = "main:1.2", Orientation = SplitOrientation.TopBottom, Size = 10 };
    private byte[] _line = [];

    /// <summary>
    /// Encodes a request line to decode in the decode benchmark.
    /// </summary>
    [GlobalSetup]
    public void Setup() => _line = ProtocolCodec.EncodeLine(ProtocolCodec.Request(1, ProtocolMethods.BlockSplit, _split, ProtocolJsonContext.Default.BlockSplitParams));

    /// <summary>
    /// Encodes a split request to a line.
    /// </summary>
    /// <returns>The encoded bytes.</returns>
    [Benchmark]
    public byte[] EncodeRequest() => ProtocolCodec.EncodeLine(ProtocolCodec.Request(1, ProtocolMethods.BlockSplit, _split, ProtocolJsonContext.Default.BlockSplitParams));

    /// <summary>
    /// Decodes a line and its parameters.
    /// </summary>
    /// <returns>The decoded parameters.</returns>
    [Benchmark]
    public BlockSplitParams DecodeRequest()
    {
        ProtocolMessage message = ProtocolCodec.DecodeLine(_line.AsSpan()[..^1]);
        return ProtocolCodec.FromElement(message.Params, ProtocolJsonContext.Default.BlockSplitParams);
    }
}
