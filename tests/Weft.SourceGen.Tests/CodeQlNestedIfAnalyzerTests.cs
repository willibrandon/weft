using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Weft.SourceGen.Tests;

/// <summary>
/// Verifies nested-condition findings and preserves control flow that cannot be combined.
/// </summary>
/// <param name="testContext">The test cancellation context.</param>
[TestClass]
public sealed class CodeQlNestedIfAnalyzerTests(TestContext testContext)
{
    /// <summary>
    /// Reports the outer keyword for directly nested braced, unbraced, and pattern conditions.
    /// </summary>
    /// <param name="body">The source statement under analysis.</param>
    [TestMethod]
    [DataRow("if (left) { if (right) { return 1; } }")]
    [DataRow("if (left) if (right) return 1;")]
    [DataRow("if (left) { { if (right) return 1; } }")]
    [DataRow("if (value is string text) { if (text.Length > 0) return 1; }")]
    public async Task ReportsCombinableNestedConditions(string body)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(body).ConfigureAwait(false);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlNestedIfAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        SyntaxTree? tree = diagnostic.Location.SourceTree;
        Assert.IsNotNull(tree);
        string source = (await tree.GetTextAsync(testContext.CancellationToken).ConfigureAwait(false)).ToString();
        Assert.AreEqual(source.IndexOf(body, StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);
        Assert.AreEqual("if", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    /// <summary>
    /// Accepts alternative branches, intermediate work, declarations, and already combined conditions.
    /// </summary>
    /// <param name="body">The source statement whose structure must remain intact.</param>
    [TestMethod]
    [DataRow("if (left) { if (right) return 1; } else return 2;")]
    [DataRow("if (left) { if (right) return 1; else return 2; }")]
    [DataRow("if (left) { System.Console.WriteLine(right); if (right) return 1; }")]
    [DataRow("if (left) { int length = value.ToString().Length; if (length > 0) return 1; }")]
    [DataRow("if (left && right) return 1;")]
    public async Task AcceptsDistinctConditionalControlFlow(string body)
    {
        Assert.IsEmpty(await AnalyzeAsync(body).ConfigureAwait(false));
    }

    /// <summary>
    /// Verifies nested ifs over a type with its own Boolean operators are left alone.
    /// </summary>
    [TestMethod]
    public async Task AcceptsNestedIfsOverCustomBooleanType()
    {
        const string Source = """
            internal readonly struct Tri
            {
                private readonly int _state;

                internal Tri(int state) => _state = state;

                public static bool operator true(Tri value) => value._state > 0;

                public static bool operator false(Tri value) => value._state <= 0;

                public static Tri operator &(Tri left, Tri right) => new(left._state * right._state);
            }

            internal static class Gate
            {
                internal static int Open(Tri first, Tri second)
                {
                    if (first)
                    {
                        if (second)
                        {
                            return 1;
                        }
                    }

                    return 0;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await CodeQlFileCompilation.AnalyzeAsync(
            Source, new CodeQlNestedIfAnalyzer(), testContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    private Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string body) => CodeQlFileCompilation.AnalyzeAsync(
        $$"""
        internal static class Conditions
        {
            internal static int Check(bool left, bool right, object value)
            {
                {{body}}
                return 0;
            }
        }
        """, new CodeQlNestedIfAnalyzer(), testContext.CancellationToken);
}
