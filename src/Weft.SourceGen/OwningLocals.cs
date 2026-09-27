using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace Weft.SourceGen;

/// <summary>
/// The locals that currently reference one tracked resource: the original and every alias copied from it.
/// </summary>
internal sealed class OwningLocals
{
    private readonly List<ILocalSymbol> _locals;

    /// <summary>
    /// Starts tracking with the local the resource was first handed to.
    /// </summary>
    /// <param name="origin">The original local.</param>
    internal OwningLocals(ILocalSymbol origin)
    {
        Origin = origin;
        _locals = [origin];
    }

    /// <summary>
    /// Gets the local the resource was first handed to.
    /// </summary>
    internal ILocalSymbol Origin { get; }

    /// <summary>
    /// Gets the declared type of the original local.
    /// </summary>
    internal ITypeSymbol Type => Origin.Type;

    /// <summary>
    /// Reports whether a symbol is one of the locals that reference the resource.
    /// </summary>
    /// <param name="symbol">The symbol an identifier resolved to.</param>
    /// <returns>Whether the symbol is an owning local.</returns>
    internal bool Contains(ISymbol? symbol) =>
        symbol is ILocalSymbol local && _locals.Any(owner => SymbolEqualityComparer.Default.Equals(owner, local));

    /// <summary>
    /// Adds a local the resource was copied into, so cleanup through it counts too.
    /// </summary>
    /// <param name="alias">The alias local.</param>
    internal void Add(ILocalSymbol alias)
    {
        if (!Contains(alias))
        {
            _locals.Add(alias);
        }
    }

    /// <summary>
    /// Drops a local that was overwritten and reports whether any local still references the resource.
    /// </summary>
    /// <param name="local">The overwritten local.</param>
    /// <returns>Whether the resource is still referenced.</returns>
    internal bool Remove(ILocalSymbol local)
    {
        _locals.RemoveAll(owner => SymbolEqualityComparer.Default.Equals(owner, local));
        return _locals.Count > 0;
    }
}
