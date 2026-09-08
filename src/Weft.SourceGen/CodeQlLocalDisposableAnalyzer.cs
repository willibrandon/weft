using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Weft.SourceGen;

/// <summary>
/// Requires explicit cleanup for disposable locals and exception-safe ownership transfers.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlLocalDisposableAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies a disposable local whose cleanup is indirect or whose transfer is unprotected.
    /// </summary>
    public const string DiagnosticId = "WEFT0008";

    private const string DisposableCollectionTypeName =
        "Weft.Debugger.DisposableCollection<T>";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Protect disposable local ownership",
        "Disposable local '{0}' requires explicit, exception-safe cleanup or ownership transfer",
        "Reliability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Transferred locals must not introduce CodeQL cs/local-not-disposed or cs/dispose-not-called-on-throw findings.");

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
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, SyntaxKind.LocalDeclarationStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLoopDeclaration, SyntaxKind.ForStatement);
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalDeclarationStatementSyntax)context.Node;
        // A declaration sits in a block or directly in a switch section; both are statement sequences.
        if (!declaration.UsingKeyword.IsKind(SyntaxKind.None) ||
            declaration.Parent is not (BlockSyntax or SwitchSectionSyntax))
        {
            return;
        }

        SyntaxNode block = declaration.Parent;

        foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
        {
            if (context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is not ILocalSymbol local)
            {
                continue;
            }

            if (variable.Initializer?.Value is { } initializer && CreatesResource(initializer) &&
                (DisposableLocalOwnership.MayLeak(local, variable, declaration, block, context) ||
                    initializer is (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax) &&
                    HasConfiguredLibraryDisposal(local, block, context)))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_rule, variable.GetLocation(), local.Name));
            }

            // Every later statement that assigns the local a resource starts its own ownership period,
            // whatever the initializer was and wherever the assignment sits in the block.
            foreach (ExpressionStatementSyntax handoff in FindResourceAssignments(local, declaration, block, context)
                .Where(handoff => LeaksFromAssignment(local, handoff, context)))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_rule, ((AssignmentExpressionSyntax)handoff.Expression).Left.GetLocation(), local.Name));
            }
        }
    }

    // A local declared by a for initializer lives for the loop, so the loop must dispose it or hand it
    // off on every path before it is left, the paths that skip the body included.
    private static void AnalyzeLoopDeclaration(SyntaxNodeAnalysisContext context)
    {
        var loop = (ForStatementSyntax)context.Node;
        if (loop.Declaration is not { } declaration)
        {
            return;
        }

        foreach (VariableDeclaratorSyntax variable in declaration.Variables.Where(variable => LeaksFromLoopDeclaration(variable, loop, context)))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, variable.GetLocation(), variable.Identifier.ValueText));
        }
    }

    private static bool LeaksFromLoopDeclaration(VariableDeclaratorSyntax variable, ForStatementSyntax loop, SyntaxNodeAnalysisContext context) =>
        variable.Initializer is { } initializer &&
        CreatesResource(initializer.Value) &&
        context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is ILocalSymbol local &&
        DisposableLocalOwnership.MayLeakInLoop(local, variable, loop, context);

    private static IEnumerable<ExpressionStatementSyntax> FindResourceAssignments(
        ILocalSymbol local,
        LocalDeclarationStatementSyntax declaration,
        SyntaxNode block,
        SyntaxNodeAnalysisContext context) =>
        block.ChildNodes()
            .OfType<StatementSyntax>()
            .SkipWhile(candidate => candidate != declaration)
            .Skip(1)
            .SelectMany(static statement => statement.DescendantNodesAndSelf(
                static node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            .OfType<ExpressionStatementSyntax>()
            .Where(statement => IsResourceAssignment(statement, local, context));

    private static bool LeaksFromAssignment(ILocalSymbol local, ExpressionStatementSyntax handoff, SyntaxNodeAnalysisContext context) =>
        handoff.Parent is (BlockSyntax or SwitchSectionSyntax) and { } scope &&
        DisposableLocalOwnership.MayLeak(local, ((AssignmentExpressionSyntax)handoff.Expression).Right, handoff, priorRisk: false, scope, context);

    private static bool IsResourceAssignment(ExpressionStatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement.Expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment &&
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
        CreatesResource(assignment.Right) &&
        SymbolEqualityComparer.Default.Equals(local, context.SemanticModel.GetSymbolInfo(target, context.CancellationToken).Symbol);

    private static bool CreatesResource(ExpressionSyntax expression) => expression switch
    {
        ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or InvocationExpressionSyntax => true,
        AwaitExpressionSyntax awaited => CreatesResource(awaited.Expression),
        ConditionalExpressionSyntax conditional => CreatesResource(conditional.WhenTrue) || CreatesResource(conditional.WhenFalse),
        ParenthesizedExpressionSyntax parenthesized => CreatesResource(parenthesized.Expression),
        _ => false
    };

    private static bool HasConfiguredLibraryDisposal(
        ILocalSymbol local,
        SyntaxNode block,
        SyntaxNodeAnalysisContext context)
    {
        if (!local.Type.DeclaringSyntaxReferences.IsEmpty ||
            local.Type.ContainingAssembly?.Name.StartsWith("Weft.", StringComparison.Ordinal) == true ||
            !local.Type.AllInterfaces.Any(static type => type.ToDisplayString() == "System.IDisposable"))
        {
            return false;
        }

        return block.DescendantNodes(static node =>
                node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<InvocationExpressionSyntax>()
            .Where(IsAsyncUsingExpression)
            .Any(invocation => invocation is
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver }
            } &&
                SymbolEqualityComparer.Default.Equals(local,
                    context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol) &&
                context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol
                {
                    Name: "ConfigureAwait",
                    ReturnType: { } returnType,
                    ContainingType: { } containingType
                } &&
                returnType.ToDisplayString() == "System.Runtime.CompilerServices.ConfiguredAsyncDisposable" &&
                containingType.ToDisplayString() == "System.Threading.Tasks.TaskAsyncEnumerableExtensions");
    }

    private static bool IsAsyncUsingExpression(InvocationExpressionSyntax invocation) =>
        invocation.Parent switch
        {
            UsingStatementSyntax statement => !statement.AwaitKeyword.IsKind(SyntaxKind.None),
            EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax declaration }
                }
            } => !declaration.AwaitKeyword.IsKind(SyntaxKind.None),
            _ => false
        };

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(
                invocation,
                context.CancellationToken).Symbol is not IMethodSymbol
                {
                    Name: "Acquire"
                } method ||
            method.ContainingType.OriginalDefinition.ToDisplayString() !=
                DisposableCollectionTypeName ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            GetLambdaValue(invocation.ArgumentList.Arguments[0].Expression) is not
                IdentifierNameSyntax identifier ||
            context.SemanticModel.GetSymbolInfo(
                identifier,
                context.CancellationToken).Symbol is not ILocalSymbol local ||
            !IsDisposable(local.Type) ||
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(
                context.CancellationToken) is not VariableDeclaratorSyntax variable ||
            variable.Initializer?.Value is not
                (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax) ||
            variable.Parent?.Parent is not LocalDeclarationStatementSyntax declaration ||
            !declaration.UsingKeyword.IsKind(SyntaxKind.None))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_rule,
            variable.GetLocation(),
            local.Name));
    }

    private static bool IsDisposable(ITypeSymbol type) =>
        type.ToDisplayString() == "System.IDisposable" ||
        type.AllInterfaces.Any(static item => item.ToDisplayString() == "System.IDisposable");

    private static ExpressionSyntax? GetLambdaValue(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } value } => value,
        SimpleLambdaExpressionSyntax { ExpressionBody: { } value } => value,
        _ => null
    };
}
