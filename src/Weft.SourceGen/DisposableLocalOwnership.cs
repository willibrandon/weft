using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Weft.SourceGen;

/// <summary>
/// Recognizes throwing operations before a constructed disposable reaches cleanup or ownership transfer.
/// </summary>
internal static class DisposableLocalOwnership
{
    /// <summary>
    /// Determines whether a local can leak before entering its cleanup region or returning ownership.
    /// </summary>
    /// <param name="local">The constructed local resource.</param>
    /// <param name="variable">The local resource declaration.</param>
    /// <param name="declaration">The containing local declaration statement.</param>
    /// <param name="block">The enclosing executable block.</param>
    /// <param name="context">The analyzer's semantic context.</param>
    /// <returns>Whether an operation can throw while the local has no exception cleanup.</returns>
    internal static bool HasUnprotectedTransfer(
        ILocalSymbol local,
        VariableDeclaratorSyntax variable,
        LocalDeclarationStatementSyntax declaration,
        BlockSyntax block,
        SyntaxNodeAnalysisContext context)
    {
        if (!IsDisposableContract(local.Type) && !local.Type.AllInterfaces.Any(IsDisposableContract))
        {
            return false;
        }

        bool mayThrow = declaration.Declaration.Variables
            .SkipWhile(candidate => candidate != variable)
            .Skip(1)
            .Any(candidate => MayThrow(candidate, context));
        foreach (StatementSyntax statement in block.Statements
            .SkipWhile(candidate => candidate != declaration)
            .Skip(1))
        {
            if (statement is TryStatementSyntax protection &&
                HasExceptionCleanup(protection, local, context))
            {
                return mayThrow;
            }

            bool priorRisk = mayThrow;
            mayThrow |= MayThrow(statement, context);
            if (ReturnsLocal(statement, local, context) || TransfersLocal(statement, local, context))
            {
                // A transfer nested under a condition covers only some paths, so the scan continues
                // for the others unless the risk already accumulated makes the transfer unsafe.
                if (mayThrow || statement is ReturnStatementSyntax or ExpressionStatementSyntax)
                {
                    return mayThrow;
                }
            }

            if (statement is ExpressionStatementSyntax && DisposesLocal(statement, local, context))
            {
                // The disposal itself is exception safe, but any earlier failure would have skipped it.
                return priorRisk;
            }
        }

        return false;
    }

    private static bool IsDisposableContract(ITypeSymbol type) =>
        type.ToDisplayString() is "System.IDisposable" or "System.IAsyncDisposable";

    private static bool TransfersLocal(StatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                Left: IdentifierNameSyntax destination,
                Right: IdentifierNameSyntax source
            }
        } && IsLocal(source, local, context) &&
        context.SemanticModel.GetSymbolInfo(destination, context.CancellationToken).Symbol is ILocalSymbol receiver &&
        !SymbolEqualityComparer.Default.Equals(receiver, local);

    private static bool HasExceptionCleanup(
        TryStatementSyntax statement,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context)
    {
        if (statement.Finally is { } cleanup && DisposesLocalOnEveryPath(cleanup.Block, local, context))
        {
            return true;
        }

        // Every handler can intercept the exception, so each must clean up, and one must catch everything.
        return statement.Catches.Count > 0 &&
            statement.Catches.All(clause => DisposesLocalOnEveryPath(clause.Block, local, context)) &&
            statement.Catches.Any(clause => clause.Filter is null &&
                (clause.Declaration is null || context.SemanticModel.GetTypeInfo(
                    clause.Declaration.Type, context.CancellationToken).Type?.ToDisplayString() == "System.Exception"));
    }

    private static bool ReturnsLocal(
        StatementSyntax statement,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context) =>
        statement.DescendantNodesAndSelf(DescendIntoExecution)
            .OfType<ReturnStatementSyntax>()
            .Any(returned => returned.Expression is IdentifierNameSyntax result &&
                IsLocal(result, local, context) ||
                returned.Expression is TupleExpressionSyntax tuple &&
                tuple.Arguments.Any(argument => argument.Expression is IdentifierNameSyntax identifier &&
                    IsLocal(identifier, local, context)));

    private static bool DisposesLocal(
        SyntaxNode scope,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context) =>
        scope.DescendantNodesAndSelf(DescendIntoExecution)
            .Any(node => node is UsingStatementSyntax { Expression: IdentifierNameSyntax scoped } &&
                IsLocal(scoped, local, context) || node is InvocationExpressionSyntax invocation &&
                invocation.ArgumentList.Arguments.Count == 0 &&
                invocation.Expression is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax receiver,
                    Name.Identifier.ValueText: "Dispose" or "DisposeAsync"
                } && IsLocal(receiver, local, context));

    private static bool DisposesLocalOnEveryPath(
        SyntaxNode scope,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context)
    {
        // Only a disposal that is a statement of the handler itself runs on every path through it;
        // one nested under a condition or loop can be skipped.
        IEnumerable<StatementSyntax> statements = scope switch
        {
            BlockSyntax block => block.Statements,
            CatchClauseSyntax handler => handler.Block.Statements,
            FinallyClauseSyntax handler => handler.Block.Statements,
            StatementSyntax statement => [statement],
            _ => []
        };
        return statements.Any(statement => statement switch
        {
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } => IsDisposeCall(invocation, local, context),
            ExpressionStatementSyntax { Expression: AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaited } } => IsDisposeCall(awaited, local, context),
            UsingStatementSyntax { Expression: IdentifierNameSyntax scoped } => IsLocal(scoped, local, context),
            _ => false
        });
    }

    private static bool IsDisposeCall(InvocationExpressionSyntax invocation, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        invocation.ArgumentList.Arguments.Count == 0 &&
        invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax receiver,
            Name.Identifier.ValueText: "Dispose" or "DisposeAsync"
        } &&
        IsLocal(receiver, local, context);

    private static bool MayThrow(SyntaxNode scope, SyntaxNodeAnalysisContext context) =>
        scope.DescendantNodesAndSelf(DescendIntoExecution).Any(node =>
            node is BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax or
                AwaitExpressionSyntax or ThrowStatementSyntax or ThrowExpressionSyntax or
                ElementAccessExpressionSyntax or CastExpressionSyntax ||
            node is MemberAccessExpressionSyntax member &&
                context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol is
                    IPropertySymbol);

    private static bool IsLocal(
        IdentifierNameSyntax identifier,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context) =>
        SymbolEqualityComparer.Default.Equals(local,
            context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol);

    private static bool DescendIntoExecution(SyntaxNode node) =>
        node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
}
