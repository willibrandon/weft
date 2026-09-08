using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;

namespace Weft.SourceGen;

/// <summary>
/// Prevents redundant nested and class-receiver upcasts reported by CodeQL.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlUselessUpcastAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies explicit upcasts that should use a destination-typed value directly.
    /// </summary>
    public const string DiagnosticId = "WEFT0016";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Remove redundant upcast",
        "Explicit conversion to '{0}' is implicit; use a destination-typed value directly",
        "CodeQuality",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Redundant upcasts must not introduce CodeQL cs/useless-upcast findings.");

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

    private static void AnalyzeCast(SyntaxNodeAnalysisContext context)
    {
        var cast = (CastExpressionSyntax)context.Node;
        if (context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type
                is not ITypeSymbol targetType)
        {
            return;
        }

        if (IsRedundantNullUpcast(cast, targetType, context))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_rule,
                cast.GetLocation(),
                targetType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            return;
        }

        SyntaxNode expression = cast;
        while (expression.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            expression = parentheses;
        }

        bool nested = expression.Parent is CastExpressionSyntax;
        bool classReceiver = targetType.TypeKind == TypeKind.Class &&
            expression.Parent is MemberAccessExpressionSyntax member && member.Expression == expression;
        if ((!nested && !classReceiver) ||
            context.SemanticModel.GetTypeInfo(cast.Expression, context.CancellationToken).Type
                is not ITypeSymbol sourceType)
        {
            return;
        }

        Conversion conversion = context.Compilation.ClassifyConversion(
            sourceType,
            targetType);
        if (!conversion.IsImplicit || conversion.IsIdentity || classReceiver && !conversion.IsReference)
        {
            return;
        }

        if (nested && expression.Parent is CastExpressionSyntax outer && FeedsUserDefinedConversion(context, outer, sourceType, targetType))
        {
            return;
        }

        if (classReceiver && expression.Parent is MemberAccessExpressionSyntax access &&
            BindsDifferentlyWithoutCast(context, access, expression, cast))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_rule,
            cast.GetLocation(),
            targetType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static bool FeedsUserDefinedConversion(
        SyntaxNodeAnalysisContext context,
        CastExpressionSyntax outer,
        ITypeSymbol sourceType,
        ITypeSymbol innerType)
    {
        // The inner cast picks which user-defined conversion the outer cast resolves to, so it is not redundant.
        if (context.SemanticModel.GetTypeInfo(outer.Type, context.CancellationToken).Type is not ITypeSymbol outerType)
        {
            return true;
        }

        return context.Compilation.ClassifyConversion(innerType, outerType).IsUserDefined ||
            context.Compilation.ClassifyConversion(sourceType, outerType).IsUserDefined;
    }

    private static bool BindsDifferentlyWithoutCast(
        SyntaxNodeAnalysisContext context,
        MemberAccessExpressionSyntax access,
        SyntaxNode castExpression,
        CastExpressionSyntax cast)
    {
        // Rebind the access with the cast removed. A different member, a hidden one or another extension overload,
        // means the cast chooses the member and stays. Overrides bind virtually, so they compare by their root.
        SyntaxNode target = access.Parent is InvocationExpressionSyntax invocation && invocation.Expression == access
            ? invocation
            : access;
        if (context.SemanticModel.GetSymbolInfo(target, context.CancellationToken).Symbol is not ISymbol bound)
        {
            return true;
        }

        var rewritten = (ExpressionSyntax)target.ReplaceNode(castExpression, cast.Expression.WithTriviaFrom(castExpression));
        ISymbol? without = context.SemanticModel.GetSpeculativeSymbolInfo(
            target.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression).Symbol;
        return without is null || !SymbolEqualityComparer.Default.Equals(Root(without), Root(bound));
    }

    private static ISymbol Root(ISymbol symbol)
    {
        while (true)
        {
            ISymbol? overridden = symbol switch
            {
                IMethodSymbol method => method.OverriddenMethod,
                IPropertySymbol property => property.OverriddenProperty,
                IEventSymbol @event => @event.OverriddenEvent,
                _ => null
            };
            if (overridden is null)
            {
                return symbol.OriginalDefinition;
            }

            symbol = overridden;
        }
    }

    private static bool IsRedundantNullUpcast(
        CastExpressionSyntax cast,
        ITypeSymbol targetType,
        SyntaxNodeAnalysisContext context)
    {
        if (!cast.Expression.IsKind(SyntaxKind.NullLiteralExpression) ||
            cast.Parent is not EqualsValueClauseSyntax equalsValue ||
            equalsValue.Parent is not VariableDeclaratorSyntax declarator ||
            context.SemanticModel.GetDeclaredSymbol(
                declarator,
                context.CancellationToken) is not ILocalSymbol local)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(local.Type, targetType);
    }
}
