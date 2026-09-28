using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Weft.SourceGen.Tests;

/// <summary>
/// Verifies the analyzers' own sources pass the CodeQL guards, which cannot run while those sources compile.
/// </summary>
/// <param name="testContext">The test cancellation context.</param>
[TestClass]
public sealed class CodeQlSelfAnalysisTests(TestContext testContext)
{
    /// <summary>
    /// Verifies every CodeQL guard accepts the Weft.SourceGen sources, which CodeQL analyzes like any others.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    public async Task AnalyzerSourcesPassTheCodeQlGuards()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        // The checkout is found by its solution file, wherever the build output lives beneath it.
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Join(repository.FullName, "Weft.slnx")))
        {
            repository = repository.Parent;
        }

        Assert.IsNotNull(repository, "The test runs from a build inside the repository checkout.");
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        List<SyntaxTree> trees = [];
        foreach (string path in Directory.EnumerateFiles(Path.Join(repository.FullName, "src", "Weft.SourceGen"),
            "*.cs"))
        {
            string text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parseOptions, path,
                cancellationToken));
        }

        // The analyzer assembly and these tests would repeat every type the sources declare.
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not expose trusted platform assemblies.");
        MetadataReference[] references =
        [
            .. trusted.Split(Path.PathSeparator)
                .Where(static assembly => !Path.GetFileNameWithoutExtension(assembly).StartsWith("Weft.",
                    StringComparison.Ordinal))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly))
        ];
        var compilation = CSharpCompilation.Create("Weft.SourceGen", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        Assert.IsEmpty(compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        DiagnosticAnalyzer[] guards =
        [
            .. typeof(CodeQlComplexConditionAnalyzer).Assembly.GetTypes()
                .Where(static type => type.Name.StartsWith("CodeQl", StringComparison.Ordinal)
                    && type.IsSubclassOf(typeof(DiagnosticAnalyzer)))
                .Select(static type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
        ];
        var options = new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null,
            concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false);
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers([.. guards], options)
            .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));
    }
}
