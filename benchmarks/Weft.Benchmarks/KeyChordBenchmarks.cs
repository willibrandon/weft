using BenchmarkDotNet.Attributes;
using Weft.Core;

namespace Weft.Benchmarks;

/// <summary>
/// Measures chord text parsing.
/// </summary>
[MemoryDiagnoser]
public class KeyChordBenchmarks
{
    /// <summary>
    /// Gets or sets the shortcut text to parse.
    /// </summary>
    [Params("alt+shift+h", "f1")]
    public string Shortcut { get; set; } = "alt+shift+h";

    /// <summary>
    /// Parses a direct shortcut with modifiers.
    /// </summary>
    /// <returns>Whether parsing succeeded.</returns>
    [Benchmark]
    public bool ParseShortcut()
    {
        return KeyChord.TryParse(Shortcut, out _);
    }
}
