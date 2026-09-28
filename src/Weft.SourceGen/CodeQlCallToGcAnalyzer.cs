using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Immutable;

namespace Weft.SourceGen;

/// <summary>
/// Prevents explicit full garbage collections, which the runtime schedules better on its own.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlCallToGcAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies a call to the parameterless <c>GC.Collect</c>.
    /// </summary>
    public const string DiagnosticId = "WEFT0032";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Let the runtime schedule garbage collection",
        "Call to 'GC.Collect()'",
        "Performance",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Explicit collections must not introduce CodeQL cs/call-to-gc findings.");

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
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;
        if (method.Name == "Collect" && method.Parameters.IsEmpty
            && method.ContainingType.ToDisplayString() == "System.GC")
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, invocation.Syntax.GetLocation()));
        }
    }
}
