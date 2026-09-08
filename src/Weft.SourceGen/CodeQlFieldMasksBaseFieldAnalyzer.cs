using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;

namespace Weft.SourceGen;

/// <summary>
/// Detects visible instance fields hiding base storage without an explicit use of that storage.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeQlFieldMasksBaseFieldAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies potentially accidental field hiding across a class hierarchy.
    /// </summary>
    public const string DiagnosticId = "WEFT0026";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId, "Distinguish hidden field storage", "Field '{0}' hides base storage without an explicit base-field access",
        "CodeQuality", DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "Accidental field hiding must not introduce CodeQL cs/field-masks-base-field findings.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

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
        context.RegisterCompilationStartAction(start =>
        {
            var fields = new ConcurrentBag<IFieldSymbol>();
            var explicitBaseAccesses = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            start.RegisterSymbolAction(symbol =>
            {
                var field = (IFieldSymbol)symbol.Symbol;
                if (!field.IsImplicitlyDeclared && !field.IsStatic && field.DeclaredAccessibility != Accessibility.Private)
                {
                    fields.Add(field);
                }
            }, SymbolKind.Field);
            start.RegisterSyntaxNodeAction(syntax =>
            {
                var access = (MemberAccessExpressionSyntax)syntax.Node;
                if (access.Expression is BaseExpressionSyntax &&
                    syntax.SemanticModel.GetSymbolInfo(access, syntax.CancellationToken).Symbol is IFieldSymbol field &&
                    syntax.ContainingSymbol?.ContainingType is INamedTypeSymbol accessingType)
                {
                    explicitBaseAccesses.TryAdd(AccessKey(accessingType, field), 0);
                }
            }, SyntaxKind.SimpleMemberAccessExpression);
            start.RegisterCompilationEndAction(end =>
            {
                foreach (IFieldSymbol field in fields)
                {
                    AnalyzeField(field, explicitBaseAccesses, end);
                }
            });
        });
    }

    private static void AnalyzeField(
        IFieldSymbol field, ConcurrentDictionary<string, byte> explicitBaseAccesses, CompilationAnalysisContext context)
    {
        for (INamedTypeSymbol? parent = field.ContainingType.BaseType; parent is not null; parent = parent.BaseType)
        {
            if (parent.GetMembers(field.Name).OfType<IFieldSymbol>().Any(inherited =>
                !inherited.IsStatic && inherited.DeclaredAccessibility != Accessibility.Private &&
                !explicitBaseAccesses.ContainsKey(AccessKey(field.ContainingType, inherited))))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_rule, field.Locations[0], field.Name));
                return;
            }
        }
    }

    private static string AccessKey(INamedTypeSymbol hidingType, IFieldSymbol inherited) =>
        hidingType.OriginalDefinition.ToDisplayString() + "|" + inherited.OriginalDefinition.ToDisplayString();
}
