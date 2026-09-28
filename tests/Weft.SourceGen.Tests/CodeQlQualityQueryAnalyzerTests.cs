using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Weft.SourceGen.Tests;

/// <summary>
/// Verifies the local guards for the CodeQL GC, float equality, precision, and unmanaged code queries.
/// </summary>
/// <param name="testContext">The running test's cooperative cancellation context.</param>
[TestClass]
public sealed class CodeQlQualityQueryAnalyzerTests(TestContext testContext)
{
    /// <summary>
    /// Verifies only the parameterless collection is reported, as CodeQL does.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    public async Task ReportsParameterlessCollect()
    {
        const string Source = """
            internal static class Memory
            {
                internal static long Measure()
                {
                    System.GC.Collect();
                    System.GC.Collect(0);
                    return System.GC.GetTotalMemory(forceFullCollection: true);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source, new CodeQlCallToGcAnalyzer())
            .ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlCallToGcAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.AreEqual(4, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    /// <summary>
    /// Verifies equality with a float or double operand is reported, including an integer converted to one.
    /// </summary>
    /// <param name="comparison">The comparison.</param>
    [TestMethod]
    [DataRow("size == 12f")]
    [DataRow("size != 12f")]
    [DataRow("ratio == count")]
    [DataRow("count == ratio")]
    [DataRow("ratio == 0")]
    public async Task ReportsFloatingPointEquality(string comparison)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Comparison(comparison),
            new CodeQlEqualityOnFloatsAnalyzer()).ConfigureAwait(false);

        Assert.AreEqual(CodeQlEqualityOnFloatsAnalyzer.DiagnosticId, Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Verifies tolerances, integer equality, and null tests of nullable doubles are accepted.
    /// </summary>
    /// <param name="comparison">The comparison.</param>
    [TestMethod]
    [DataRow("System.Math.Abs(size - 12f) < 0.01f")]
    [DataRow("count == 12")]
    [DataRow("maybe == null")]
    [DataRow("size < ratio")]
    public async Task AcceptsToleranceAndIntegerComparisons(string comparison)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Comparison(comparison),
            new CodeQlEqualityOnFloatsAnalyzer()).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies integer division or multiplication that reaches a floating point or decimal value is reported.
    /// </summary>
    /// <param name="expression">The converted expression.</param>
    [TestMethod]
    [DataRow("index / width * height")]
    [DataRow("offset + (index / width)")]
    [DataRow("(double)(index / width)")]
    [DataRow("(double)(index * width)")]
    [DataRow("(decimal)(index / 3)")]
    public async Task ReportsConvertedIntegerArithmetic(string expression)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Arithmetic(expression),
            new CodeQlLossOfPrecisionAnalyzer()).ConfigureAwait(false);

        Assert.AreEqual(CodeQlLossOfPrecisionAnalyzer.DiagnosticId, Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Verifies integer arithmetic kept in integers, exact constant division, and small constant products pass.
    /// </summary>
    /// <param name="expression">The converted expression.</param>
    [TestMethod]
    [DataRow("row * height")]
    [DataRow("(double)(10 / 5)")]
    [DataRow("(double)(3 * 4)")]
    [DataRow("index / (double)width")]
    public async Task AcceptsExplicitIntegerArithmetic(string expression)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Arithmetic(expression),
            new CodeQlLossOfPrecisionAnalyzer()).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies calls to external declarations are reported and calls through managed methods are not.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    public async Task ReportsExternalCalls()
    {
        const string Source = """
            using System.Runtime.InteropServices;

            internal static class Native
            {
                [DllImport("kernel32.dll")]
                private static extern int GetTickCount();

                internal static int Wrapped() => GetTickCount();

                internal static int Twice() => Wrapped() + Wrapped();
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source, new CodeQlCallToUnmanagedCodeAnalyzer())
            .ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlCallToUnmanagedCodeAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.AreEqual(7, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    /// <summary>
    /// Verifies a cast that only repeats a collection expression's type is reported, and a typed local is not.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    public async Task ReportsCollectionExpressionCastToItsOwnType()
    {
        const string Source = """
            #nullable enable
            internal static class Sizes
            {
                internal static int Sum()
                {
                    int total = 0;
                    foreach (string name in (string[])["tile", "logo"])
                    {
                        total += name.Length;
                    }

                    foreach (int size in (int[])[16, 24, 32])
                    {
                        total += size;
                    }

                    int[] more = [48, 64];
                    foreach (int size in more)
                    {
                        total += size;
                    }

                    return total;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source, new CodeQlUselessCastToSelfAnalyzer())
            .ConfigureAwait(false);

        Assert.HasCount(2, diagnostics);
        Assert.IsTrue(diagnostics.All(static diagnostic =>
            diagnostic.Id == CodeQlUselessCastToSelfAnalyzer.DiagnosticId));
        Assert.AreSequenceEqual([6, 11], diagnostics.Select(static diagnostic =>
            diagnostic.Location.GetLineSpan().StartLinePosition.Line).Order());
    }

    /// <summary>
    /// Verifies a disposable bound by a pattern and disposed after another call is reported.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    public async Task ReportsPatternBoundDisposableDisposedAfterACall()
    {
        const string Source = """
            using System.IO;

            internal static class Frames
            {
                internal static MemoryStream? Open() => new MemoryStream();

                internal static void Keep(System.Collections.Generic.List<MemoryStream> kept)
                {
                    if (Open() is { } stream && !Store(kept, stream))
                    {
                        stream.Dispose();
                    }
                }

                private static bool Store(System.Collections.Generic.List<MemoryStream> kept, MemoryStream stream)
                {
                    kept.Add(stream);
                    return true;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source, new CodeQlLocalDisposableAnalyzer())
            .ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.AreEqual(8, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    /// <summary>
    /// Verifies a pattern-bound disposable that is disposed before any other call is accepted.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    public async Task AcceptsPatternBoundDisposableDisposedFirst()
    {
        const string Source = """
            using System.IO;

            internal static class Frames
            {
                internal static MemoryStream? Open() => new MemoryStream();

                internal static void Discard()
                {
                    if (Open() is { } stream)
                    {
                        stream.Dispose();
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source, new CodeQlLocalDisposableAnalyzer())
            .ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    private static string Comparison(string comparison)
    {
        return $$"""
            internal static class Measures
            {
                internal static bool Test(float size, double ratio, int count, double? maybe)
                {
                    return {{comparison}};
                }
            }
            """;
    }

    private static string Arithmetic(string expression)
    {
        return $$"""
            internal static class Cells
            {
                internal static object Place(int index, int width, float height, int row, double offset)
                {
                    return {{expression}};
                }
            }
            """;
    }

    private Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, DiagnosticAnalyzer analyzer)
    {
        return CodeQlFileCompilation.AnalyzeAsync(source, analyzer, testContext.CancellationToken);
    }
}
