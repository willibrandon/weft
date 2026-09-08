using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Weft.SourceGen.Tests;

/// <summary>
/// Verifies the local guard for CodeQL missed-readonly findings.
/// </summary>
/// <param name="testContext">The test cancellation context.</param>
[TestClass]
public sealed class CodeQlMissedReadonlyModifierAnalyzerTests(TestContext testContext)
{
    /// <summary>
    /// Verifies a field assigned only by the constructor is reported.
    /// </summary>
    [TestMethod]
    public async Task ReportsFieldAssignedOnlyByConstructor()
    {
        const string Source = """
            internal sealed class Holder
            {
                private int _value;

                public Holder(int value)
                {
                    _value = value;
                }

                internal int Value => _value;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlMissedReadonlyModifierAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a field written from a lambda the constructor merely creates is not treated as initialized once.
    /// </summary>
    [TestMethod]
    public async Task AcceptsFieldWrittenFromConstructorLambda()
    {
        const string Source = """
            using System;
            using System.Threading;

            internal sealed class Counter
            {
                private int _count;
                private readonly Action _increment;

                public Counter()
                {
                    _increment = () => Interlocked.Increment(ref _count);
                }

                internal int Count => Volatile.Read(ref _count);

                internal void Bump() => _increment();
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    private Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) => CodeQlFileCompilation.AnalyzeAsync(
        source, new CodeQlMissedReadonlyModifierAnalyzer(), testContext.CancellationToken);
}
