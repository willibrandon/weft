using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
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
        // Only a local that owns its resource is tracked: one handed something by a construction or a
        // factory. A lookup returns something owned elsewhere, and a task is disposable in name only.
        if (!IsDisposableContract(local.Type) && !local.Type.AllInterfaces.Any(IsDisposableContract) ||
            IsTask(local.Type) || !CreatesOwnedResource(variable.Initializer?.Value, context))
        {
            return false;
        }

        bool mayThrow = declaration.Declaration.Variables
            .SkipWhile(candidate => candidate != variable)
            .Skip(1)
            .Any(candidate => MayThrow(candidate, local, context));
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
            mayThrow |= MayThrow(statement, local, context);
            if (EndsOwnership(statement, local, context))
            {
                // A plain disposal is exception safe itself, so only an earlier failure could skip it; any
                // other statement may still fail before the point where the local changes hands.
                return IsDisposalStatement(statement, local, context) ? priorRisk : mayThrow;
            }

            if (mayThrow && HandsOff(statement, local, context))
            {
                // Some path through the statement hands the local off after work that can throw.
                return true;
            }
        }

        // Reaching the end still owning the resource leaks it on every path.
        return true;
    }

    // A construction, a File factory, or a static factory of the resource's own type hands the local a
    // resource of its own; another call commonly returns something owned elsewhere.
    private static bool CreatesOwnedResource(ExpressionSyntax? initializer, SyntaxNodeAnalysisContext context) =>
        initializer switch
        {
            BaseObjectCreationExpressionSyntax => true,
            ParenthesizedExpressionSyntax parenthesized => CreatesOwnedResource(parenthesized.Expression, context),
            AwaitExpressionSyntax awaited => CreatesOwnedResource(awaited.Expression, context),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax inner, Name.Identifier.ValueText: "ConfigureAwait" } } =>
                CreatesOwnedResource(inner, context),
            InvocationExpressionSyntax invocation =>
                context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method && IsFactory(method),
            ConditionalExpressionSyntax choice => CreatesOwnedResource(choice.WhenTrue, context) || CreatesOwnedResource(choice.WhenFalse, context),
            _ => false
        };

    private static bool IsFactory(IMethodSymbol method) =>
        method.IsStatic &&
        (method.ContainingType is { Name: "File", ContainingNamespace.Name: "IO" } ||
            SymbolEqualityComparer.Default.Equals(method.ContainingType, Unwrap(method.ReturnType)));

    private static ITypeSymbol Unwrap(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Task" or "ValueTask", TypeArguments.Length: 1 } wrapped ? wrapped.TypeArguments[0] : type;

    // Ownership ends on every path through the statement when the local is disposed or handed off on each
    // of them; a branch that neither disposes nor hands off, or that may not run at all, keeps it owned.
    private static bool EndsOwnership(StatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement switch
        {
            BlockSyntax block => block.Statements.Any(inner => EndsOwnership(inner, local, context)),
            ExpressionStatementSyntax expression => IsDisposalStatement(expression, local, context) || HandsOff(expression.Expression, local, context),
            LocalDeclarationStatementSyntax declaration => IsDisposalStatement(declaration, local, context) || HandsOff(declaration.Declaration, local, context),
            ReturnStatementSyntax returned => HandsOff(returned, local, context),
            UsingStatementSyntax scoped => IsDisposalStatement(scoped, local, context) || HandsOff(scoped.Expression, local, context) ||
                HandsOff(scoped.Declaration, local, context) || EndsOwnership(scoped.Statement, local, context),
            IfStatementSyntax conditional => HandsOff(conditional.Condition, local, context) ||
                EndsOwnership(conditional.Statement, local, context) && conditional.Else is { } other && EndsOwnership(other.Statement, local, context),
            SwitchStatementSyntax selection => HandsOff(selection.Expression, local, context) || EndsOwnershipInEverySection(selection, local, context),
            ForEachStatementSyntax loop => HandsOff(loop.Expression, local, context),
            WhileStatementSyntax loop => HandsOff(loop.Condition, local, context),
            LockStatementSyntax guarded => EndsOwnership(guarded.Statement, local, context),
            TryStatementSyntax attempt => EndsOwnership(attempt.Block, local, context) ||
                attempt.Finally is { } cleanup && EndsOwnership(cleanup.Block, local, context),
            _ => false
        };

    private static bool EndsOwnershipInEverySection(SwitchStatementSyntax selection, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        selection.Sections.Any(static section => section.Labels.Any(static label => label is DefaultSwitchLabelSyntax)) &&
        selection.Sections.All(section => section.Statements.Any(inner => EndsOwnership(inner, local, context)));

    // Passing the local on, storing it, returning it, or capturing it may hand its ownership elsewhere.
    private static bool HandsOff(SyntaxNode? scope, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        scope is not null && scope.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => IsLocal(identifier, local, context))
            .Any(static identifier => identifier.Parent switch
            {
                ArgumentSyntax or EqualsValueClauseSyntax or ReturnStatementSyntax or YieldStatementSyntax => true,
                InitializerExpressionSyntax or ExpressionElementSyntax or CastExpressionSyntax or AnonymousFunctionExpressionSyntax => true,
                AssignmentExpressionSyntax assignment => assignment.Right == identifier,
                ConditionalExpressionSyntax choice => choice.WhenTrue == identifier || choice.WhenFalse == identifier,
                BinaryExpressionSyntax binary => binary.IsKind(SyntaxKind.CoalesceExpression),
                _ => false
            });

    private static bool IsDisposalStatement(StatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement switch
        {
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } => IsDisposeCall(invocation, local, context),
            ExpressionStatementSyntax { Expression: AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaited } } => IsDisposeCall(awaited, local, context),
            ExpressionStatementSyntax { Expression: ConditionalAccessExpressionSyntax guarded } => IsGuardedDisposeCall(guarded, local, context),
            UsingStatementSyntax { Expression: { } scoped } => IsLocalOrConfigured(scoped, local, context),
            LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } declaration =>
                declaration.Declaration.Variables.Any(variable => variable.Initializer is { } initializer &&
                    IsLocalOrConfigured(initializer.Value, local, context)),
            _ => false
        };

    private static bool IsGuardedDisposeCall(ConditionalAccessExpressionSyntax guarded, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        guarded is
        {
            Expression: IdentifierNameSyntax receiver,
            WhenNotNull: InvocationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 0,
                Expression: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Dispose" or "DisposeAsync" }
            }
        } && IsLocal(receiver, local, context);

    private static bool DisposesInFinally(TryStatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement.Finally is { } cleanup && DisposesLocalOnEveryPath(cleanup.Block, local, context);

    private static bool IsDisposableContract(ITypeSymbol type) =>
        type.ToDisplayString() is "System.IDisposable" or "System.IAsyncDisposable";

    // A task is disposable in name only; nothing disposes one, and the CodeQL queries do not ask for it.
    private static bool IsTask(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Threading.Tasks.Task")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasExceptionCleanup(
        TryStatementSyntax statement,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context)
    {
        if (DisposesInFinally(statement, local, context))
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

    // Only a disposal that is a statement of the block itself runs on every path through it; one nested
    // under a condition or loop can be skipped.
    private static bool DisposesLocalOnEveryPath(BlockSyntax block, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        block.Statements.Any(statement => IsDisposalStatement(statement, local, context));

    private static bool IsDisposeCall(InvocationExpressionSyntax invocation, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        invocation.Expression switch
        {
            // The ConfigureAwait wrapper around an async disposal is still that disposal.
            MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax inner, Name.Identifier.ValueText: "ConfigureAwait" } =>
                IsDisposeCall(inner, local, context),
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver, Name.Identifier.ValueText: "Dispose" or "DisposeAsync" } =>
                invocation.ArgumentList.Arguments.Count == 0 && IsLocal(receiver, local, context),
            _ => false
        };

    // A call that takes the local, directly or inside a wrapper or a lambda, is the handoff itself.
    private static bool ReceivesLocal(ArgumentListSyntax? arguments, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        arguments is not null && arguments.Arguments.Any(argument =>
            argument.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(identifier => IsLocal(identifier, local, context)));

    // Neither the callee of the call that receives the local nor the target it is stored into runs after
    // the handoff has started, so nothing in them counts as risk.
    private static IEnumerable<TextSpan> HandoffSpans(SyntaxNode node, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        node switch
        {
            AssignmentExpressionSyntax { Right: IdentifierNameSyntax stored } assignment when IsLocal(stored, local, context) => [assignment.Left.Span],
            InvocationExpressionSyntax invocation when ReceivesLocal(invocation.ArgumentList, local, context) => [invocation.Expression.Span],
            _ => []
        };

    // Disposing the local itself cannot leak it, and a call handed the local owns it from then on, so
    // neither counts as risk.
    private static bool MayThrow(SyntaxNode scope, ILocalSymbol local, SyntaxNodeAnalysisContext context)
    {
        List<SyntaxNode> nodes = [.. scope.DescendantNodesAndSelf(DescendIntoExecution)];
        List<TextSpan> handoffs = [.. nodes.SelectMany(node => HandoffSpans(node, local, context))];
        return nodes
            .Where(node => !handoffs.Any(span => span.Contains(node.Span)))
            .Any(node => node switch
            {
                InvocationExpressionSyntax invocation => !IsDisposeCall(invocation, local, context) && !ReceivesLocal(invocation.ArgumentList, local, context),
                AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaited } => !IsDisposeCall(awaited, local, context) && !ReceivesLocal(awaited.ArgumentList, local, context),
                BaseObjectCreationExpressionSyntax creation => !ReceivesLocal(creation.ArgumentList, local, context),
                AwaitExpressionSyntax or ThrowStatementSyntax or ThrowExpressionSyntax => true,
                ElementAccessExpressionSyntax or CastExpressionSyntax => true,
                MemberAccessExpressionSyntax member => context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol is IPropertySymbol,
                _ => false
            });
    }

    private static bool IsLocal(
        IdentifierNameSyntax identifier,
        ILocalSymbol local,
        SyntaxNodeAnalysisContext context) =>
        SymbolEqualityComparer.Default.Equals(local,
            context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol);

    private static bool DescendIntoExecution(SyntaxNode node) =>
        node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
}
