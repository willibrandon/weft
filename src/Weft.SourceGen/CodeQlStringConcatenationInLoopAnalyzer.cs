using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Weft.SourceGen;

/// <summary>
/// Detects string accumulation into variables that survive successive loop iterations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlStringConcatenationInLoopAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies repeated string concatenation that should use a mutable string builder.
    /// </summary>
    public const string DiagnosticId = "WEFT0027";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId, "Use a string builder for loop accumulation",
        "Use StringBuilder instead of accumulating string '{0}' with concatenation in a loop",
        "CodeQuality", DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "String accumulation must not introduce CodeQL cs/string-concatenation-in-loop findings.");

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
        context.RegisterOperationAction(AnalyzeAssignment, OperationKind.CompoundAssignment, OperationKind.SimpleAssignment);
    }

    private static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        var assignment = (IAssignmentOperation)context.Operation;
        if (assignment.Target.Type?.SpecialType != SpecialType.System_String ||
            GetVariable(assignment.Target) is not ISymbol variable ||
            !IsConcatenation(assignment) ||
            !SurvivesLoop(assignment, variable))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(s_rule, assignment.Syntax.GetLocation(), variable.Name));
    }

    private static bool IsConcatenation(IAssignmentOperation assignment) =>
        assignment is ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null } ||
        assignment is ISimpleAssignmentOperation &&
        assignment.Value is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String } addition &&
        (ContainsStorage(addition.LeftOperand, assignment.Target) || ContainsStorage(addition.RightOperand, assignment.Target));

    private static bool ContainsStorage(IOperation operand, IOperation target)
    {
        while (operand is IConversionOperation { OperatorMethod: null } conversion)
        {
            operand = conversion.Operand;
        }

        return SameStorage(operand, target) ||
            operand is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String } addition &&
            (ContainsStorage(addition.LeftOperand, target) || ContainsStorage(addition.RightOperand, target));
    }

    private static bool SameStorage(IOperation left, IOperation right)
    {
        // The same field on a different instance is different storage, so the receivers must match as well.
        while (left is IConversionOperation { OperatorMethod: null } l)
        {
            left = l.Operand;
        }

        while (right is IConversionOperation { OperatorMethod: null } r)
        {
            right = r.Operand;
        }

        return (left, right) switch
        {
            (ILocalReferenceOperation a, ILocalReferenceOperation b) => SymbolEqualityComparer.Default.Equals(a.Local, b.Local),
            (IParameterReferenceOperation a, IParameterReferenceOperation b) => SymbolEqualityComparer.Default.Equals(a.Parameter, b.Parameter),
            (IFieldReferenceOperation a, IFieldReferenceOperation b) =>
                SymbolEqualityComparer.Default.Equals(a.Field, b.Field) && SameReceiver(a.Instance, b.Instance),
            _ => false
        };
    }

    private static bool SameReceiver(IOperation? left, IOperation? right) =>
        (left is null or IInstanceReferenceOperation) && (right is null or IInstanceReferenceOperation) ||
        left is not null && right is not null && SameStorage(left, right);

    private static bool FlowsIntoBody(IOperation assignment, ILoopOperation loop, ISymbol variable)
    {
        if (variable is IFieldSymbol)
        {
            // Data flow does not track fields; a plain assignment earlier in the same body discards the old value.
            return !ResetBeforeAppend(assignment, loop, variable);
        }

        // The whole loop statement counts, so a for loop's condition and incrementors are read as well.
        SemanticModel? model = assignment.SemanticModel ?? loop.SemanticModel;
        if (loop.Syntax is not StatementSyntax statement || model is null)
        {
            return true;
        }

        DataFlowAnalysis? flow = model.AnalyzeDataFlow(statement);
        return flow is null || !flow.Succeeded ||
            flow.DataFlowsIn.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, variable));
    }

    private static bool ResetBeforeAppend(IOperation assignment, ILoopOperation loop, ISymbol variable)
    {
        if (loop.Body is not IBlockOperation body)
        {
            return false;
        }

        foreach (IOperation statement in body.Operations)
        {
            if (statement.Syntax.Span.Contains(assignment.Syntax.Span))
            {
                return false;
            }

            if (statement is IExpressionStatementOperation { Operation: ISimpleAssignmentOperation reset } &&
                SymbolEqualityComparer.Default.Equals(GetVariable(reset.Target), variable))
            {
                return true;
            }
        }

        return false;
    }

    private static ISymbol? GetVariable(IOperation operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IFieldReferenceOperation field => field.Field,
        _ => null
    };

    private static bool SurvivesLoop(IOperation assignment, ISymbol variable)
    {
        for (IOperation? ancestor = assignment.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return false;
            }

            if (ancestor is ILoopOperation loop &&
                (variable is not ILocalSymbol || !variable.DeclaringSyntaxReferences.Any(reference =>
                    reference.SyntaxTree == loop.Syntax.SyntaxTree && loop.Syntax.Span.Contains(reference.Span))))
            {
                // A value reset before it is read in each iteration never grows across iterations.
                return FlowsIntoBody(assignment, loop, variable);
            }
        }

        return false;
    }
}
