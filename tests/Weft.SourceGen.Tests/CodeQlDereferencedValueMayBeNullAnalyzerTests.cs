using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Weft.SourceGen.Tests;

/// <summary>
/// Verifies the local guard for CodeQL dereferenced-value-may-be-null findings.
/// </summary>
/// <param name="testContext">The test cancellation context.</param>
[TestClass]
public sealed class CodeQlDereferencedValueMayBeNullAnalyzerTests(TestContext testContext)
{
    /// <summary>
    /// Verifies a guard that only tests null on one side of a disjunction does not excuse the dereference.
    /// </summary>
    [TestMethod]
    public async Task ReportsDereferenceUnderDisjunctiveGuard()
    {
        const string Source = """
            using System.Collections.Generic;

            internal static class Lookup
            {
                internal static int Length(Dictionary<string, string> map, string key, bool force)
                {
                    if (map.TryGetValue(key, out string? value) && (value is not null || force))
                    {
                        return value!.Length;
                    }

                    return 0;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlDereferencedValueMayBeNullAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a guard that conjoins the null test still excuses the dereference.
    /// </summary>
    [TestMethod]
    public async Task AcceptsDereferenceUnderConjunctiveGuard()
    {
        const string Source = """
            using System.Collections.Generic;

            internal static class Lookup
            {
                internal static int Length(Dictionary<string, string> map, string key, bool force)
                {
                    if (map.TryGetValue(key, out string? value) && value is not null && force)
                    {
                        return value!.Length;
                    }

                    return 0;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    private Task<ImmutableArray<Diagnostic>> RunAsync(string source) => CodeQlFileCompilation.AnalyzeAsync(
        source, new CodeQlDereferencedValueMayBeNullAnalyzer(), testContext.CancellationToken);
}
