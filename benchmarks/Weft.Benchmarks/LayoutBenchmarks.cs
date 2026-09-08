using BenchmarkDotNet.Attributes;
using Weft.Core;

namespace Weft.Benchmarks;

/// <summary>
/// Measures layout tree operations at a realistic block count.
/// </summary>
[MemoryDiagnoser]
public class LayoutBenchmarks
{
    private readonly BlockId[] _blocks = [.. Enumerable.Range(1, 12).Select(i => new BlockId(i))];
    private LayoutTree _tree = new(LayoutOptions.Framed);
    private string _serialized = string.Empty;

    /// <summary>
    /// Builds a twelve-block tiled layout to operate on.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _tree = new LayoutTree(LayoutOptions.Framed);
        _tree.ApplyPreset(LayoutPreset.Tiled, _blocks, 200, 60);
        _serialized = _tree.Serialize();
    }

    /// <summary>
    /// Fits the layout to a slightly different size and back.
    /// </summary>
    [Benchmark]
    public void FitResize()
    {
        _tree.Fit(190, 58);
        _tree.Fit(200, 60);
    }

    /// <summary>
    /// Rebuilds the tiled preset from scratch.
    /// </summary>
    [Benchmark]
    public void ApplyTiledPreset()
    {
        var tree = new LayoutTree(LayoutOptions.Framed);
        tree.ApplyPreset(LayoutPreset.Tiled, _blocks, 200, 60);
    }

    /// <summary>
    /// Serializes the layout to its checksummed string.
    /// </summary>
    /// <returns>The layout string.</returns>
    [Benchmark]
    public string Serialize() => _tree.Serialize();

    /// <summary>
    /// Parses the layout string back into a tree.
    /// </summary>
    /// <returns>Whether parsing succeeded.</returns>
    [Benchmark]
    public bool Parse() => LayoutSerializer.TryParse(_serialized, out _);

    /// <summary>
    /// Computes geometry for every block.
    /// </summary>
    /// <returns>The placements.</returns>
    [Benchmark]
    public IReadOnlyList<BlockGeometry> Geometry() => _tree.ToGeometry();
}
