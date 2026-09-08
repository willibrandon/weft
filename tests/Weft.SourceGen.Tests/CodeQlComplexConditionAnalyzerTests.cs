using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Weft.SourceGen.Tests;

/// <summary>
/// Verifies the local guard for CodeQL complex-condition findings.
/// </summary>
/// <param name="testContext">The test cancellation context.</param>
[TestClass]
public sealed class CodeQlComplexConditionAnalyzerTests(TestContext testContext)
{
    /// <summary>
    /// Verifies a Boolean condition with too many groups is reported.
    /// </summary>
    [TestMethod]
    public async Task ReportsComplexBooleanCondition()
    {
        const string Source = """
            internal static class Gate
            {
                internal static bool Open(bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h) =>
                    (a && b) || (c && d) || (e && f) || (g && h) || (a && c) || (b && d);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlComplexConditionAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a long integral mask expression is not treated as a complex Boolean condition.
    /// </summary>
    [TestMethod]
    public async Task AcceptsIntegralMasks()
    {
        const string Source = """
            internal static class Masks
            {
                internal static int Combine(int a, int b, int c, int d, int e, int f, int g, int h) =>
                    (a & b) | (c & d) | (e & f) | (g & h) | (a & c) | (b & d);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    private Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) => CodeQlFileCompilation.AnalyzeAsync(
        source, new CodeQlComplexConditionAnalyzer(), testContext.CancellationToken);
}
