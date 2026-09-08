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
    /// <param name="scope">The block or switch section that holds the statement.</param>
    /// <param name="context">The analyzer's semantic context.</param>
    /// <returns>Whether the local can leak on some path through the block.</returns>
    internal static bool MayLeak(
        ILocalSymbol local,
        VariableDeclaratorSyntax variable,
        LocalDeclarationStatementSyntax declaration,
        SyntaxNode scope,
        SyntaxNodeAnalysisContext context)
    {
        bool laterRisk = declaration.Declaration.Variables
            .SkipWhile(candidate => candidate != variable)
            .Skip(1)
            .Any(candidate => MayThrow(candidate, local, context));
        return MayLeak(local, variable.Initializer?.Value, declaration, laterRisk, scope, context);
    }

    /// <summary>
    /// Determines whether a local handed a resource by the given statement can leak from that point on.
    /// </summary>
    /// <param name="local">The local that receives the resource.</param>
    /// <param name="value">The expression that produces the resource.</param>
    /// <param name="origin">The statement that hands the resource to the local, or null to start at the scope's first statement.</param>
    /// <param name="priorRisk">Whether the origin statement can still fail after the local holds the resource.</param>
    /// <param name="scope">The block or switch section that holds the statement.</param>
    /// <param name="context">The analyzer's semantic context.</param>
    /// <returns>Whether the local can leak on some path through the block.</returns>
    internal static bool MayLeak(
        ILocalSymbol local,
        ExpressionSyntax? value,
        StatementSyntax? origin,
        bool priorRisk,
        SyntaxNode scope,
        SyntaxNodeAnalysisContext context)
    {
        // Only a local that owns its resource is tracked: one handed something by a construction or a
        // factory. A lookup returns something owned elsewhere, and a task is disposable in name only.
        if (!IsDisposableContract(local.Type) && !local.Type.AllInterfaces.Any(IsDisposableContract) ||
            IsTask(local.Type) || !CreatesOwnedResource(value, context))
        {
            return false;
        }

        bool mayThrow = priorRisk;
        StatementSyntax? cursor = origin;
        while (true)
        {
            foreach (StatementSyntax statement in StatementsOf(scope)
                .SkipWhile(candidate => cursor is not null && candidate != cursor)
                .Skip(cursor is null ? 0 : 1))
            {
                if (statement is TryStatementSyntax protection &&
                    HasExceptionCleanup(protection, local, context))
                {
                    // A disposing finally ends ownership. Catch-only cleanup covers the try's own failures, so
                    // the scan continues on the normal path unless the try itself disposes or hands off the
                    // local, or leaves early while still owning it.
                    if (DisposesInFinally(protection, local, context) ||
                        EndsOwnership(protection.Block, breakLeaves: true, throwLeaves: false, local, context))
                    {
                        return mayThrow;
                    }

                    if (LeaksOnExit(protection.Block, breakLeaves: true, throwLeaves: false, local, context))
                    {
                        return true;
                    }

                    continue;
                }

                if (EndsOwnership(statement, local, context))
                {
                    // Only a failure before the point where ownership ends can still leak the local.
                    return mayThrow || RiskUntilOwnershipEnds(statement, local, context);
                }

                mayThrow |= MayThrow(statement, local, context);

                if (mayThrow && HandsOff(statement, local, context))
                {
                    // Some path through the statement hands the local off after work that can throw.
                    return true;
                }

                if (LeaksOnExit(statement, breakLeaves: true, throwLeaves: true, local, context))
                {
                    // Some path leaves the block early while the local is still owned.
                    return true;
                }
            }

            // The end of a nested block hands the scan to the enclosing block, after the statement that
            // owns the nested one; a loop body is the exception, since its next iteration starts over.
            // Reaching the end of the outermost block still owning the resource leaks it on every path.
            StatementSyntax? enclosing = EnclosingStatement(scope);
            SyntaxNode? outer = enclosing?.Parent;
            if (enclosing is null || outer is not (BlockSyntax or SwitchSectionSyntax))
            {
                return true;
            }

            if (enclosing is TryStatementSyntax attempt && scope.Parent is TryStatementSyntax or CatchClauseSyntax &&
                HasExceptionCleanup(attempt, local, context))
            {
                // Leaving a try that cleans up on failure: a disposing finally ends ownership for good, and
                // handler cleanup covers every failure inside, so the normal path continues without that risk.
                if (DisposesInFinally(attempt, local, context))
                {
                    return false;
                }

                mayThrow = false;
            }

            cursor = enclosing;
            scope = outer;
        }
    }

    private static SyntaxList<StatementSyntax> StatementsOf(SyntaxNode scope) =>
        scope switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => default
        };

    private static StatementSyntax? EnclosingStatement(SyntaxNode scope)
    {
        SyntaxNode? parent = scope.Parent;
        while (parent is ElseClauseSyntax or CatchClauseSyntax or FinallyClauseSyntax or SwitchSectionSyntax)
        {
            parent = parent.Parent;
        }

        return parent is StatementSyntax statement &&
            statement is not (WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax)
            ? statement
            : null;
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

    private static bool EndsOwnership(StatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        EndsOwnership(statement, breakLeaves: true, throwLeaves: true, local, context);

    // Ownership ends on every path through the statement when the local is disposed or handed off on each
    // of them; a branch that neither disposes nor hands off, or that may not run at all, keeps it owned.
    // The flags say whether a break or a throw leaves the sequence under analysis while still owning it.
    private static bool EndsOwnership(StatementSyntax statement, bool breakLeaves, bool throwLeaves, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement switch
        {
            BlockSyntax block => EndsOwnership(block.Statements, breakLeaves, throwLeaves, local, context),
            ExpressionStatementSyntax expression => IsDisposalStatement(expression, local, context) || HandsOff(expression.Expression, local, context),
            LocalDeclarationStatementSyntax declaration => IsDisposalStatement(declaration, local, context) || HandsOff(declaration.Declaration, local, context),
            ReturnStatementSyntax returned => HandsOff(returned, local, context),
            UsingStatementSyntax scoped => IsDisposalStatement(scoped, local, context) || HandsOff(scoped.Expression, local, context) ||
                HandsOff(scoped.Declaration, local, context) || EndsOwnership(scoped.Statement, breakLeaves, throwLeaves, local, context),
            IfStatementSyntax conditional => HandsOff(conditional.Condition, local, context) ||
                EndsOwnershipOnBothBranches(conditional, breakLeaves, throwLeaves, local, context),
            SwitchStatementSyntax selection => HandsOff(selection.Expression, local, context) || EndsOwnershipInEverySection(selection, throwLeaves, local, context),
            ForEachStatementSyntax loop => HandsOff(loop.Expression, local, context),
            WhileStatementSyntax loop => HandsOff(loop.Condition, local, context),
            LockStatementSyntax guarded => EndsOwnership(guarded.Statement, breakLeaves, throwLeaves, local, context),
            TryStatementSyntax attempt => EndsOwnership(attempt.Block, breakLeaves, throwLeaves, local, context) ||
                attempt.Finally is { } cleanup && EndsOwnership(cleanup.Block, breakLeaves, throwLeaves, local, context),
            _ => false
        };

    private static bool EndsOwnershipOnBothBranches(IfStatementSyntax conditional, bool breakLeaves, bool throwLeaves, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        EndsOwnership(conditional.Statement, breakLeaves, throwLeaves, local, context) &&
        conditional.Else is { } other && EndsOwnership(other.Statement, breakLeaves, throwLeaves, local, context);

    private static bool EndsOwnershipInEverySection(SwitchStatementSyntax selection, bool throwLeaves, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        selection.Sections.Any(static section => section.Labels.Any(static label => label is DefaultSwitchLabelSyntax)) &&
        selection.Sections.All(section => EndsOwnership(section.Statements, breakLeaves: false, throwLeaves, local, context));

    // A sequence ends ownership when a statement does so before any path leaves the sequence early.
    private static bool EndsOwnership(IEnumerable<StatementSyntax> statements, bool breakLeaves, bool throwLeaves, ILocalSymbol local, SyntaxNodeAnalysisContext context)
    {
        foreach (StatementSyntax statement in statements)
        {
            if (EndsOwnership(statement, breakLeaves, throwLeaves, local, context))
            {
                return true;
            }

            if (LeaksOnExit(statement, breakLeaves, throwLeaves, local, context))
            {
                return false;
            }
        }

        return false;
    }

    // A path that leaves the statement early, by a return, a throw, or a jump out of the enclosing
    // sequence, leaks the local unless ownership ended before it. A break inside a switch only leaves
    // the switch, and a throw inside a try whose handlers dispose the local is covered.
    private static bool LeaksOnExit(StatementSyntax statement, bool breakLeaves, bool throwLeaves, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement switch
        {
            BreakStatementSyntax => breakLeaves,
            ContinueStatementSyntax => true,
            ThrowStatementSyntax => throwLeaves,
            ReturnStatementSyntax or GotoStatementSyntax => !EndsOwnership(statement, local, context),
            BlockSyntax block => LeaksOnExit(block.Statements, breakLeaves, throwLeaves, local, context),
            IfStatementSyntax conditional => !HandsOff(conditional.Condition, local, context) &&
                LeaksOnEitherBranch(conditional, breakLeaves, throwLeaves, local, context),
            SwitchStatementSyntax selection => !HandsOff(selection.Expression, local, context) &&
                selection.Sections.Any(section => LeaksOnExit(section.Statements, breakLeaves: false, throwLeaves, local, context)),
            WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax => LeavesEnclosing(statement, throwLeaves, local, context),
            TryStatementSyntax attempt => LeaksOnExit(attempt.Block, breakLeaves, throwLeaves, local, context) ||
                attempt.Catches.Any(handler => LeaksOnExit(handler.Block, breakLeaves, throwLeaves, local, context)) ||
                attempt.Finally is { } cleanup && LeaksOnExit(cleanup.Block, breakLeaves, throwLeaves, local, context),
            UsingStatementSyntax scoped => LeaksOnExit(scoped.Statement, breakLeaves, throwLeaves, local, context),
            LockStatementSyntax guarded => LeaksOnExit(guarded.Statement, breakLeaves, throwLeaves, local, context),
            CheckedStatementSyntax region => LeaksOnExit(region.Block, breakLeaves, throwLeaves, local, context),
            _ => false
        };

    private static bool LeaksOnEitherBranch(IfStatementSyntax conditional, bool breakLeaves, bool throwLeaves, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        LeaksOnExit(conditional.Statement, breakLeaves, throwLeaves, local, context) ||
        conditional.Else is { } other && LeaksOnExit(other.Statement, breakLeaves, throwLeaves, local, context);

    private static bool LeaksOnExit(IEnumerable<StatementSyntax> statements, bool breakLeaves, bool throwLeaves, ILocalSymbol local, SyntaxNodeAnalysisContext context)
    {
        foreach (StatementSyntax statement in statements)
        {
            if (EndsOwnership(statement, breakLeaves, throwLeaves, local, context))
            {
                return false;
            }

            if (LeaksOnExit(statement, breakLeaves, throwLeaves, local, context))
            {
                return true;
            }
        }

        return false;
    }

    // Only a return, a throw, or a goto leaves a loop for the enclosing sequence; break and continue stay in it.
    private static bool LeavesEnclosing(StatementSyntax loop, bool throwLeaves, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        loop.DescendantNodes(DescendIntoExecution).Any(node =>
            node is GotoStatementSyntax ||
            node is ThrowStatementSyntax && throwLeaves ||
            node is ReturnStatementSyntax returned && !EndsOwnership(returned, local, context));

    // The risk met before ownership ends, on some path through a statement that ends it on every path;
    // work that runs only after the local was disposed or handed off cannot leak it.
    private static bool RiskUntilOwnershipEnds(StatementSyntax statement, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        statement switch
        {
            BlockSyntax block => RiskUntilOwnershipEnds(block.Statements, local, context),
            ExpressionStatementSyntax expression => !IsDisposalStatement(expression, local, context) && MayThrow(expression, local, context),
            LocalDeclarationStatementSyntax declaration => !IsDisposalStatement(declaration, local, context) && MayThrow(declaration, local, context),
            UsingStatementSyntax scoped => !IsDisposalStatement(scoped, local, context) && MayThrow(scoped, local, context),
            IfStatementSyntax conditional => MayThrow(conditional.Condition, local, context) ||
                !HandsOff(conditional.Condition, local, context) && RiskOnEitherBranch(conditional, local, context),
            SwitchStatementSyntax selection => MayThrow(selection.Expression, local, context) ||
                !HandsOff(selection.Expression, local, context) && selection.Sections.Any(section => RiskUntilOwnershipEnds(section.Statements, local, context)),
            // A finally that ends ownership covers every failure in the try; only its own work before that point counts.
            TryStatementSyntax attempt => attempt.Finally is { } cleanup && EndsOwnership(cleanup.Block, local, context)
                ? RiskUntilOwnershipEnds(cleanup.Block, local, context)
                : RiskUntilOwnershipEnds(attempt.Block, local, context),
            LockStatementSyntax guarded => RiskUntilOwnershipEnds(guarded.Statement, local, context),
            ForEachStatementSyntax loop => MayThrow(loop.Expression, local, context),
            WhileStatementSyntax loop => MayThrow(loop.Condition, local, context),
            _ => MayThrow(statement, local, context)
        };

    private static bool RiskOnEitherBranch(IfStatementSyntax conditional, ILocalSymbol local, SyntaxNodeAnalysisContext context) =>
        RiskUntilOwnershipEnds(conditional.Statement, local, context) ||
        conditional.Else is { } other && RiskUntilOwnershipEnds(other.Statement, local, context);

    private static bool RiskUntilOwnershipEnds(IEnumerable<StatementSyntax> statements, ILocalSymbol local, SyntaxNodeAnalysisContext context)
    {
        foreach (StatementSyntax statement in statements)
        {
            if (EndsOwnership(statement, local, context))
            {
                return RiskUntilOwnershipEnds(statement, local, context);
            }

            if (MayThrow(statement, local, context))
            {
                return true;
            }
        }

        return false;
    }

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

    // Cleanup counts only when the block ends ownership before anything in it can fail or leave early;
    // a disposal after a call that may throw, or after an exit, is not reached on every path.
    private static bool DisposesLocalOnEveryPath(BlockSyntax block, ILocalSymbol local, SyntaxNodeAnalysisContext context)
    {
        foreach (StatementSyntax statement in block.Statements)
        {
            if (EndsOwnership(statement, local, context))
            {
                return !RiskUntilOwnershipEnds(statement, local, context);
            }

            if (MayThrow(statement, local, context) || LeaksOnExit(statement, breakLeaves: true, throwLeaves: true, local, context))
            {
                return false;
            }
        }

        return false;
    }

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
