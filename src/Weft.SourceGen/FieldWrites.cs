using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Weft.SourceGen;

/// <summary>
/// Decides whether a field reference writes the field, changes it in place, or hands out a writable alias to it.
/// </summary>
internal static class FieldWrites
{
    /// <summary>
    /// Reports whether the reference writes the field, including struct member writes and writable ref escapes.
    /// </summary>
    /// <param name="reference">The field reference to classify.</param>
    /// <returns>Whether the reference counts as a write.</returns>
    internal static bool IsWrite(IFieldReferenceOperation reference) => IsWrittenThrough(reference, reference.Field.Type);

    private static bool IsWrittenThrough(IOperation reference, ITypeSymbol type)
    {
        IOperation current = reference;
        while (current.Parent is IConversionOperation or IParenthesizedOperation or ITupleOperation)
        {
            current = current.Parent;
        }

        // A mutable struct is changed in place through a non-readonly call or a member write on it; a
        // readonly field would hand those a defensive copy and lose the update.
        if (IsMutableStruct(type))
        {
            switch (current.Parent)
            {
                case IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current) && !invocation.TargetMethod.IsReadOnly:
                    return true;
                case IMemberReferenceOperation member when ReferenceEquals(member.Instance, current):
                    return member.Type is { } memberType && IsWrittenThrough(member, memberType);
            }
        }

        return current.Parent switch
        {
            IAssignmentOperation assignment => ReferenceEquals(assignment.Target, current),
            IIncrementOrDecrementOperation increment => ReferenceEquals(increment.Target, current),
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            IAddressOfOperation => true,
            IReturnOperation returned => ReferenceEquals(returned.ReturnedValue, current) && ReturnsWritableReference(returned),
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.Ref } } => true,
            _ => false
        };
    }

    private static bool IsMutableStruct(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsValueType: true, IsReadOnly: false, EnumUnderlyingType: null } &&
            type.SpecialType == SpecialType.None ||
        type is ITypeParameterSymbol { HasValueTypeConstraint: true };

    // A writable ref return aliases the field for every caller, which a readonly field cannot allow.
    private static bool ReturnsWritableReference(IReturnOperation returned) =>
        returned.SemanticModel?.GetEnclosingSymbol(returned.Syntax.SpanStart) is IMethodSymbol { RefKind: RefKind.Ref };
}
