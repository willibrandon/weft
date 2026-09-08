using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Weft.SourceGen;

/// <summary>
/// Requires explicit nullable capture and flow proof before dereferencing values.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlDereferencedValueMayBeNullAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies a nullable out variable dereference that requires explicit flow proof.
    /// </summary>
    public const string DiagnosticId = "WEFT0015";

    /// <summary>
    /// Identifies nullable properties that must be captured before extracting their value.
    /// </summary>
    public const string NullablePropertyDiagnosticId = "WEFT0023";

    /// <summary>
    /// Identifies asserted nullable locals that require a typed capture before unwrapping.
    /// </summary>
    public const string NullableLocalDiagnosticId = "WEFT0028";

    private static readonly DiagnosticDescriptor s_nullableLocalRule = new(
        NullableLocalDiagnosticId,
        "Capture asserted nullable locals as their underlying type",
        "Capture asserted nullable local '{0}' with a typed assertion, pattern, or null-coalescing throw before unwrapping it",
        "CodeQuality",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Typed nullable captures preserve explicit value proof across subsequent work and suspension.");

    private static readonly DiagnosticDescriptor s_nullablePropertyRule = new(
        NullablePropertyDiagnosticId,
        "Capture nullable properties before unwrapping",
        "Capture nullable property '{0}' with a pattern or null-coalescing throw before unwrapping it",
        "CodeQuality",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Nullable property values require an explicit single-read capture instead of assertion-only proof.");

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Prove nullable out variables before dereferencing",
        "Nullable out variable '{0}' is force-dereferenced outside its declaring guard",
        "CodeQuality",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Nullable out variables must not introduce CodeQL cs/dereferenced-value-may-be-null findings.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [s_rule, s_nullablePropertyRule, s_nullableLocalRule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeSuppression,
            SyntaxKind.SuppressNullableWarningExpression);
        context.RegisterSyntaxNodeAction(AnalyzeNullableProperty, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeNullableProperty(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        if (access.Name.Identifier.ValueText != "Value" ||
            context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol is not IPropertySymbol value ||
            value.ContainingType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
        {
            return;
        }

        ExpressionSyntax receiver = access.Expression;
        while (receiver is ParenthesizedExpressionSyntax parenthesized)
        {
            receiver = parenthesized.Expression;
        }

        if (context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol is IPropertySymbol property)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_nullablePropertyRule, access.GetLocation(), property.Name));
        }
        else if (context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol is ILocalSymbol local &&
            HasPrecedingNullAssertion(access, local, context))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_nullableLocalRule, access.GetLocation(), local.Name));
        }
    }

    private static bool HasPrecedingNullAssertion(MemberAccessExpressionSyntax access, ILocalSymbol local,
        SyntaxNodeAnalysisContext context)
    {
        foreach (SyntaxNode ancestor in access.Ancestors())
        {
            if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or
                AccessorDeclarationSyntax or BaseMethodDeclarationSyntax)
            {
                break;
            }

            if (ancestor is not BlockSyntax block)
            {
                continue;
            }

            foreach (StatementSyntax statement in block.Statements)
            {
                if (statement.Span.End >= access.SpanStart)
                {
                    break;
                }

                if (statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } &&
                    invocation.ArgumentList.Arguments.Count > 0 &&
                    context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method &&
                    method.Name == "IsNotNull" &&
                    method.ContainingType.ToDisplayString() == "Microsoft.VisualStudio.TestTools.UnitTesting.Assert" &&
                    SymbolEquals(invocation.ArgumentList.Arguments[0].Expression, local, context))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AnalyzeSuppression(SyntaxNodeAnalysisContext context)
    {
        var suppression = (PostfixUnaryExpressionSyntax)context.Node;
        if (suppression.Operand is not IdentifierNameSyntax identifier ||
            context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is not
                ILocalSymbol local ||
            local.NullableAnnotation != NullableAnnotation.Annotated ||
            local.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(context.CancellationToken))
                .OfType<SingleVariableDesignationSyntax>()
                .FirstOrDefault(IsOutVariable) is null ||
            IsProtectedByExplicitNullGuard(suppression, local, context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_rule,
            suppression.GetLocation(),
            local.Name));
    }

    private static bool IsOutVariable(SingleVariableDesignationSyntax declaration) =>
        declaration.FirstAncestorOrSelf<ArgumentSyntax>()?.RefOrOutKeyword.IsKind(
            SyntaxKind.OutKeyword) == true;

    private static bool IsProtectedByExplicitNullGuard(
        PostfixUnaryExpressionSyntax suppression,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context) =>
        suppression.Ancestors()
            .OfType<IfStatementSyntax>()
            .Any(statement =>
                statement.Statement.Span.Contains(suppression.Span) &&
                ImpliesNotNull(statement.Condition, local, context) &&
                !WrittenBeforeSuppression(statement.Statement, suppression, local, context));

    private static bool WrittenBeforeSuppression(
        StatementSyntax guarded,
        PostfixUnaryExpressionSyntax suppression,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context)
    {
        // A write to the local anywhere in the guarded region up to the suppression voids the guard's proof.
        StatementSyntax first = guarded;
        StatementSyntax last = guarded;
        if (guarded is BlockSyntax block && block.Statements.Count > 0)
        {
            first = block.Statements[0];
            last = block.Statements.LastOrDefault(statement => statement.Span.Start <= suppression.Span.Start) ?? first;
        }

        DataFlowAnalysis? flow = context.SemanticModel.AnalyzeDataFlow(first, last);
        return flow is null || !flow.Succeeded ||
            flow.WrittenInside.Any(written => SymbolEqualityComparer.Default.Equals(written, local));
    }

    private static bool ImpliesNotNull(ExpressionSyntax condition, ILocalSymbol local, SyntaxNodeAnalysisContext context)
    {
        // Only the Boolean structure decides: a conjunction needs one side, a disjunction needs both.
        switch (condition)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ImpliesNotNull(parenthesized.Expression, local, context);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalAndExpression } and:
                return ImpliesNotNull(and.Left, local, context) || ImpliesNotNull(and.Right, local, context);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalOrExpression } or:
                return ImpliesNotNull(or.Left, local, context) && ImpliesNotNull(or.Right, local, context);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.NotEqualsExpression } unequal:
                return unequal.Right.IsKind(SyntaxKind.NullLiteralExpression) && SymbolEquals(unequal.Left, local, context) ||
                    unequal.Left.IsKind(SyntaxKind.NullLiteralExpression) && SymbolEquals(unequal.Right, local, context);
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } negation:
                return ImpliesNull(negation.Operand, local, context);
            case IsPatternExpressionSyntax pattern:
                return pattern.Pattern is UnaryPatternSyntax
                {
                    RawKind: (int)SyntaxKind.NotPattern,
                    Pattern: ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression },
                } && SymbolEquals(pattern.Expression, local, context);
            default:
                return false;
        }
    }

    private static bool ImpliesNull(ExpressionSyntax condition, ILocalSymbol local, SyntaxNodeAnalysisContext context)
    {
        switch (condition)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ImpliesNull(parenthesized.Expression, local, context);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression } equal:
                return equal.Right.IsKind(SyntaxKind.NullLiteralExpression) && SymbolEquals(equal.Left, local, context) ||
                    equal.Left.IsKind(SyntaxKind.NullLiteralExpression) && SymbolEquals(equal.Right, local, context);
            case IsPatternExpressionSyntax pattern:
                return pattern.Pattern is ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression } &&
                    SymbolEquals(pattern.Expression, local, context);
            default:
                return false;
        }
    }

    private static bool SymbolEquals(
        ExpressionSyntax expression,
        ISymbol symbol,
        SyntaxNodeAnalysisContext context) =>
        SymbolEqualityComparer.Default.Equals(
            context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol,
            symbol);
}
