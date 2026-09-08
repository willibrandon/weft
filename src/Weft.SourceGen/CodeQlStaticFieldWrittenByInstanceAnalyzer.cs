using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Immutable;

namespace Weft.SourceGen;

/// <summary>
/// Detects instance members writing shared fields declared by their own type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlStaticFieldWrittenByInstanceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies shared field writes made through instance member implementations.
    /// </summary>
    public const string DiagnosticId = "WEFT0030";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId, "Initialize shared fields through static members",
        "Instance member '{0}' writes static field '{1}' declared by its own type",
        "CodeQuality", DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "Instance-owned initialization must not introduce CodeQL cs/static-field-written-by-instance findings.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeFieldReference, OperationKind.FieldReference);
    }

    private static void AnalyzeFieldReference(OperationAnalysisContext context)
    {
        var reference = (IFieldReferenceOperation)context.Operation;
        if (!reference.Field.IsStatic ||
            context.ContainingSymbol is not IMethodSymbol { IsStatic: false } method ||
            !SymbolEqualityComparer.Default.Equals(
                reference.Field.ContainingType.OriginalDefinition, method.ContainingType.OriginalDefinition) ||
            !FieldWrites.IsWrite(reference))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_rule, reference.Syntax.GetLocation(), method.Name, reference.Field.Name));
    }
}
