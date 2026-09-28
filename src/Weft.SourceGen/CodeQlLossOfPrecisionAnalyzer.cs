using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Immutable;
using System.Globalization;

namespace Weft.SourceGen;

/// <summary>
/// Prevents integer division or multiplication whose result silently flows into a floating point value.
/// </summary>
/// <remarks>
/// An integer quotient loses its fraction, and an integer product can overflow, before the conversion happens.
/// Computing the integer part into a named integer first, or converting an operand, makes the intent explicit.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlLossOfPrecisionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies an integer division or multiplication converted to a floating point or decimal type.
    /// </summary>
    public const string DiagnosticId = "WEFT0034";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Make integer arithmetic explicit before converting it",
        "{0}",
        "Reliability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Integer arithmetic converted to a floating point or decimal type must not introduce CodeQL "
            + "cs/loss-of-precision findings.");

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
        context.RegisterOperationAction(AnalyzeBinary, OperationKind.Binary);
    }

    private static void AnalyzeBinary(OperationAnalysisContext context)
    {
        var operation = (IBinaryOperation)context.Operation;
        if (operation.OperatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Multiply)
            || operation.OperatorMethod is not null || !IsIntegral(operation.Type)
            || ConvertedType(operation) is not { } converted)
        {
            return;
        }

        if (operation.OperatorKind == BinaryOperatorKind.Divide)
        {
            if (!IsExactDivision(operation))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_rule, operation.Syntax.GetLocation(),
                    "Possible loss of precision: any fraction will be lost."));
            }
        }
        else if (!IsSmallProduct(operation))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, operation.Syntax.GetLocation(),
                "Possible overflow: result of integer multiplication cast to "
                + converted.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "."));
        }
    }

    /// <summary>
    /// Follows the value through addition, subtraction, and multiplication to a floating point or decimal conversion.
    /// </summary>
    private static ITypeSymbol? ConvertedType(IOperation operation)
    {
        IOperation current = operation;
        while (current.Parent is { } parent)
        {
            switch (parent)
            {
                case IParenthesizedOperation:
                    current = parent;
                    continue;
                case IConversionOperation conversion when IsFloatingOrDecimal(conversion.Type):
                    return conversion.Type;
                case IBinaryOperation binary when binary.OperatorKind is BinaryOperatorKind.Add
                    or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply:
                    current = parent;
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }

    private static bool IsExactDivision(IBinaryOperation division)
    {
        return Constant(division.LeftOperand) is long numerator && Constant(division.RightOperand) is long denominator
            && denominator != 0 && numerator % denominator == 0;
    }

    private static bool IsSmallProduct(IBinaryOperation product)
    {
        if (Constant(product.LeftOperand) is not long left || Constant(product.RightOperand) is not long right)
        {
            return false;
        }

        decimal result = (decimal)left * right;
        SpecialType type = product.Type?.SpecialType ?? SpecialType.None;
        return (type == SpecialType.System_Int32 && result is >= int.MinValue and <= int.MaxValue)
            || (type == SpecialType.System_UInt32 && result is >= uint.MinValue and <= uint.MaxValue)
            || (type == SpecialType.System_Int64 && result is >= long.MinValue and <= long.MaxValue);
    }

    private static long? Constant(IOperation operand)
    {
        IOperation value = operand;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return value.ConstantValue is { HasValue: true, Value: { } constant } && IsIntegral(value.Type)
            ? Convert.ToInt64(constant, CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsIntegral(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16
            or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Char;
    }

    private static bool IsFloatingOrDecimal(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double
            or SpecialType.System_Decimal;
    }
}
