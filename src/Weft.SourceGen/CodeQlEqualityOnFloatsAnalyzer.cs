using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;

namespace Weft.SourceGen;

/// <summary>
/// Prevents exact equality tests on floating point values, which rounding makes unreliable.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlEqualityOnFloatsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies an equality or inequality test with a float or double operand.
    /// </summary>
    public const string DiagnosticId = "WEFT0033";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Compare floating point values within a tolerance",
        "Equality checks on floating point values can yield unexpected results",
        "Reliability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Equality on float or double operands must not introduce CodeQL cs/equality-on-floats findings.");

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
        context.RegisterSyntaxNodeAction(AnalyzeEquality, SyntaxKind.EqualsExpression,
            SyntaxKind.NotEqualsExpression);
    }

    private static void AnalyzeEquality(SyntaxNodeAnalysisContext context)
    {
        var equality = (BinaryExpressionSyntax)context.Node;
        if (equality.Left.IsKind(SyntaxKind.NullLiteralExpression)
            || equality.Right.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return;
        }

        // Each operand is judged after its implicit conversion, as CodeQL sees it, so an integer compared with a
        // double counts as a floating point comparison too.
        if (IsFloatingPoint(context, equality.Left) || IsFloatingPoint(context, equality.Right))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, equality.GetLocation()));
        }
    }

    private static bool IsFloatingPoint(SyntaxNodeAnalysisContext context, ExpressionSyntax operand)
    {
        ITypeSymbol? type = context.SemanticModel.GetTypeInfo(operand, context.CancellationToken).ConvertedType;
        return type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
    }
}
