using BenchmarkDotNet.Running;

namespace Weft.Benchmarks;

/// <summary>
/// Entry point that runs the selected benchmarks.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the benchmarks named on the command line, or presents the switcher.
    /// </summary>
    /// <param name="args">BenchmarkDotNet arguments.</param>
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
