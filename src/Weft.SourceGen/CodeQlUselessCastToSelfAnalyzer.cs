using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;

namespace Weft.SourceGen;

/// <summary>
/// Prevents explicit casts whose operand already has the target type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlUselessCastToSelfAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies explicit casts that do not change the operand type.
    /// </summary>
    public const string DiagnosticId = "WEFT0019";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Remove cast to the same type",
        "Expression already has type '{0}'",
        "CodeQuality",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Identity casts must not introduce CodeQL cs/useless-cast-to-self findings.");

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
        context.RegisterSyntaxNodeAction(AnalyzeCast, SyntaxKind.CastExpression);
    }

    private static bool IsReceiver(CastExpressionSyntax cast)
    {
        SyntaxNode expression = cast;
        while (expression.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            expression = parentheses;
        }

        return expression.Parent switch
        {
            MemberAccessExpressionSyntax member => member.Expression == expression,
            ElementAccessExpressionSyntax element => element.Expression == expression,
            ConditionalAccessExpressionSyntax conditional => conditional.Expression == expression,
            _ => false
        };
    }

    private static void AnalyzeCast(SyntaxNodeAnalysisContext context)
    {
        var cast = (CastExpressionSyntax)context.Node;
        ITypeSymbol? sourceType = context.SemanticModel.GetTypeInfo(
            cast.Expression,
            context.CancellationToken).Type;
        ITypeSymbol? targetType = context.SemanticModel.GetTypeInfo(
            cast.Type,
            context.CancellationToken).Type;
        if (sourceType is null ||
            targetType is null ||
            !SymbolEqualityComparer.IncludeNullability.Equals(sourceType, targetType))
        {
            return;
        }

        if (targetType.IsValueType && IsReceiver(cast))
        {
            // The cast yields a copy, so a mutating member call leaves the original untouched; removing it would not.
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_rule,
            cast.GetLocation(),
            targetType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }
}
