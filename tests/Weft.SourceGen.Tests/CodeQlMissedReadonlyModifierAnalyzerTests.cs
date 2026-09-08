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

    /// <summary>
    /// Verifies a field handed out by writable reference is not asked to become readonly.
    /// </summary>
    /// <param name="member">A member that exposes the field by ref.</param>
    [TestMethod]
    [DataRow("internal ref int Value => ref _value;")]
    [DataRow("internal ref int Get() { return ref _value; }")]
    [DataRow("internal void Bump() { ref int alias = ref _value; alias++; }")]
    public async Task AcceptsFieldExposedByWritableReference(string member)
    {
        string source = $$"""
            internal sealed class Holder
            {
                private int _value;

                public Holder(int value)
                {
                    _value = value;
                }

                {{member}}
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a field returned by readonly reference still needs the modifier.
    /// </summary>
    [TestMethod]
    public async Task ReportsFieldExposedByReadonlyReference()
    {
        const string Source = """
            internal sealed class Holder
            {
                private int _value;

                public Holder(int value)
                {
                    _value = value;
                }

                internal ref readonly int Value => ref _value;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlMissedReadonlyModifierAnalyzer.DiagnosticId, diagnostic.Id);
    }

    private Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) => CodeQlFileCompilation.AnalyzeAsync(
        source, new CodeQlMissedReadonlyModifierAnalyzer(), testContext.CancellationToken);
    /// <summary>
    /// Verifies a required field, which consumers set through initializers, is not asked to be readonly.
    /// </summary>
    [TestMethod]
    public async Task AcceptsRequiredField()
    {
        const string Source = """
            internal sealed class Options
            {
                public required int Value;

                internal int Doubled => Value * 2;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a field written through deconstruction counts as written.
    /// </summary>
    [TestMethod]
    public async Task AcceptsFieldWrittenByDeconstruction()
    {
        const string Source = """
            internal sealed class Pair
            {
                private int _value;

                public Pair()
                {
                    _value = 0;
                }

                internal void Refresh()
                {
                    (_value, _) = Read();
                }

                internal int Value => _value;

                private static (int, int) Read() => (1, 2);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a field holding a mutable struct that is changed through a method call is not asked to be readonly.
    /// </summary>
    [TestMethod]
    public async Task AcceptsMutableStructFieldChangedThroughCall()
    {
        const string Source = """
            internal struct Counter
            {
                private int _count;

                internal void Increment() => _count++;

                internal readonly int Count => _count;
            }

            internal sealed class Tally
            {
                private Counter _counter;

                internal void Bump() => _counter.Increment();

                internal int Count => _counter.Count;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a field of a struct-constrained type parameter mutated through an interface call is not reported.
    /// </summary>
    [TestMethod]
    public async Task AcceptsConstrainedStructFieldChangedThroughCall()
    {
        const string Source = """
            internal interface IMutable
            {
                void Increment();
            }

            internal sealed class Holder<T> where T : struct, IMutable
            {
                private T _value;

                internal void Bump() => _value.Increment();

                internal T Value => _value;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    private Task<ImmutableArray<Diagnostic>> RunAsync(string source) => CodeQlFileCompilation.AnalyzeAsync(
        source, new CodeQlMissedReadonlyModifierAnalyzer(), testContext.CancellationToken);
}
