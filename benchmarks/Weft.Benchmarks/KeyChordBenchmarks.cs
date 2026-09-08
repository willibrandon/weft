using BenchmarkDotNet.Attributes;
using Weft.Core;

namespace Weft.Benchmarks;

/// <summary>
/// Measures chord text parsing.
/// </summary>
[MemoryDiagnoser]
public class KeyChordBenchmarks
{
    private KeyChord _leader = new([]);

    /// <summary>
    /// Parses the default leader.
    /// </summary>
    [GlobalSetup]
    public void Setup() => KeyChord.TryParse("ctrl+b", null, out _leader);

    /// <summary>
    /// Parses a leader-prefixed chord with a shifted letter.
    /// </summary>
    /// <returns>Whether parsing succeeded.</returns>
    [Benchmark]
    public bool ParseLeaderChord() => KeyChord.TryParse("leader shift+h", _leader, out _);
}
