using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Weft.SourceGen;

/// <summary>
/// Recognizes a constructed disposable that can leak: a throw before its cleanup or transfer, or no cleanup at all.
/// </summary>
internal static class DisposableLocalOwnership
{
    /// <summary>
    /// Determines whether a local can leak, before its cleanup or transfer or by staying owned at the block's end.
    /// </summary>
    /// <param name="local">The constructed local resource.</param>
    /// <param name="variable">The local resource declaration.</param>
    /// <param name="declaration">The containing local declaration statement.</param>
    /// <param name="block">The enclosing executable block.</param>
    /// <param name="context">The analyzer's semantic context.</param>
    /// <returns>Whether the local can leak on some path through the block.</returns>
    internal static bool MayLeak(
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
                // A disposing finally ends ownership. Catch-only cleanup covers the try's own failures, so
                // the scan continues on the normal path unless the try itself disposes or hands off the local.
                if (DisposesInFinally(protection, local, context) || EndsOwnership(protection.Block, local, context))
                {
                    return mayThrow;
                }

                continue;
            }

            bool priorRisk = mayThrow;
            mayThrow |= MayThrow(statement, context);
            if (EndsWithTransfer(statement, local, mayThrow, context))
            {
                return mayThrow;
            }

            if (DisposesOnNormalPath(statement, local, context))
            {
                // The disposal itself is exception safe, but any earlier failure would have skipped it.
                return priorRisk;
            }
        }

        // Reaching the end still owning a resource the local was handed leaks it on every path.
        return CreatesOwnedResource(variable.Initializer?.Value, context) && StaysOwned(block, declaration, local, context);
    }

    // Only a construction or a well-known factory hands the local a resource of its own; another call
    // commonly returns something owned elsewhere, which is not a leak here.
    private static bool CreatesOwnedResource(ExpressionSyntax? initializer, SyntaxNodeAnalysisContext context) =>
        initializer switch
        {
            BaseObjectCreationExpressionSyntax => true,
            InvocationExpressionSyntax invocation => context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is
                IMethodSymbol { IsStatic: true, ContainingType: { Name: "File", ContainingNamespace.Name: "IO" } },
            ConditionalExpressionSyntax choice => CreatesOwnedResource(choice.WhenTrue, context) || CreatesOwnedResource(choice.WhenFalse, context),
            _ => false
        };

    private static bool DisposesInFinally(TryStatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement.Finally is { } cleanup && DisposesLocalOnEveryPath(cleanup.Block, local, context);

    private static bool EndsOwnership(BlockSyntax block, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        DisposesLocal(block, local, context) || ReturnsLocal(block, local, context) ||
        block.Statements.Any(statement => TransfersLocal(statement, local, context));

    // A handler only runs on failure, so a disposal inside one does not end ownership on the normal path.
    private static bool DisposesOnNormalPath(StatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement switch
        {
            TryStatementSyntax attempt => DisposesLocal(attempt.Block, local, context) ||
                attempt.Finally is { } cleanup && DisposesLocal(cleanup.Block, local, context),
            _ => DisposesLocal(statement, local, context)
        };

    // A reference that only reads a member or compares with null keeps the local owned here; passing it
    // on, storing it, returning it, or capturing it may hand the ownership elsewhere.
    private static bool StaysOwned(
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context) =>
        block.Statements
            .SkipWhile(candidate => candidate != declaration)
            .SelectMany(static statement => statement.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Where(identifier => IsLocal(identifier, local, context))
            .All(static identifier => identifier.Parent switch
            {
                MemberAccessExpressionSyntax member => member.Expression == identifier,
                ConditionalAccessExpressionSyntax conditional => conditional.Expression == identifier,
                ElementAccessExpressionSyntax element => element.Expression == identifier,
                BinaryExpressionSyntax comparison => comparison.IsKind(SyntaxKind.EqualsExpression) || comparison.IsKind(SyntaxKind.NotEqualsExpression),
                IsPatternExpressionSyntax => true,
                _ => false
            });

    // A transfer nested under a condition covers only some paths, so the scan continues for the
    // others unless the risk already accumulated makes the transfer unsafe on its own.
    private static bool EndsWithTransfer(
        StatementSyntax statement,
        ILocalSymbol local,
        bool mayThrow,
        SyntaxNodeAnalysisContext context) =>
        (ReturnsLocal(statement, local, context) || TransfersLocal(statement, local, context)) &&
        (mayThrow || statement is ReturnStatementSyntax or ExpressionStatementSyntax);

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
        scope.DescendantNodesAndSelf(DescendIntoExecution).Any(node => IsDisposal(node, local, context));

    private static bool IsDisposal(SyntaxNode node, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        node switch
        {
            UsingStatementSyntax { Expression: { } scoped } => IsLocalOrConfigured(scoped, local, context),
            LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } declaration =>
                declaration.Declaration.Variables.Any(variable => variable.Initializer is { } initializer &&
                    IsLocalOrConfigured(initializer.Value, local, context)),
            InvocationExpressionSyntax invocation => IsDisposeCall(invocation, local, context),
            ConditionalAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax guarded,
                WhenNotNull: InvocationExpressionSyntax
                {
                    ArgumentList.Arguments.Count: 0,
                    Expression: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Dispose" or "DisposeAsync" }
                }
            } => IsLocal(guarded, local, context),
            _ => false
        };

    // A using over the local, or over its ConfigureAwait wrapper, disposes it when the scope ends.
    private static bool IsLocalOrConfigured(ExpressionSyntax expression, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        expression switch
        {
            IdentifierNameSyntax identifier => IsLocal(identifier, local, context),
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver, Name.Identifier.ValueText: "ConfigureAwait" }
            } => IsLocal(receiver, local, context),
            _ => false
        };

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
