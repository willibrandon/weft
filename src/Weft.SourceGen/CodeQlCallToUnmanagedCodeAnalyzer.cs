using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Immutable;

namespace Weft.SourceGen;

/// <summary>
/// Prevents calls that go straight to an external native function instead of a managed wrapper.
/// </summary>
/// <remarks>
/// A <c>LibraryImport</c> declaration normally compiles to a generated managed stub. When a signature needs no
/// marshalling and no error capture, the generator instead forwards the partial method to an external
/// declaration, and calls to it reach unmanaged code directly.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlCallToUnmanagedCodeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies a call to an external method declared in a class.
    /// </summary>
    public const string DiagnosticId = "WEFT0035";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Call native code through a managed wrapper",
        "Call to external method '{0}' reaches unmanaged code directly",
        "Interoperability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Calls to external methods must not introduce CodeQL cs/call-to-unmanaged-code findings.");

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
        bool external = method.IsExtern || method.PartialImplementationPart?.IsExtern == true;
        if (external && method.ContainingType.TypeKind == TypeKind.Class)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }
}
