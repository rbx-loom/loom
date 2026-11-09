using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    public override Type VisitStaticBlock(StaticBlock staticBlock)
    {
        var interfaceType = Visit(staticBlock.InterfaceName);
        // A generic interface's own name, visited as a TypeName, comes back either as the bare GenericType
        // (no arguments could be filled in) or - whenever every parameter has a default, as is the common
        // case - as a real InstantiatedType built from those defaults. Either way this needs the interface's
        // *substituted* property list, not the raw template: a member signature that self-references the
        // interface (a static 'make' returning 'Box') was already resolved against the default the same way
        // at declaration time, so comparing a static block body's real, inferred instantiation against that
        // requires the same substitution here too - the raw template's bare type parameters would otherwise
        // never structurally match a concrete field the checker just inferred. Expand() is exactly that
        // substitution, the same one ExpandBareGenericType applies for the sibling member-access problem.
        interfaceType = interfaceType switch
        {
            GenericType genericInterfaceType => ExpandBareGenericType(genericInterfaceType),
            InstantiatedType instantiatedInterfaceType => instantiatedInterfaceType.Expand(),
            _ => interfaceType
        };

        if (interfaceType is not InterfaceType nonGenericInterfaceType)
            return BindType(staticBlock, PrimitiveType.Never);

        var staticProperties = nonGenericInterfaceType.ObjectType.Properties
            .Where(property => property.IsStatic)
            .ToDictionary(property => property.Name);

        CheckDuplicateStaticMembers(staticBlock.Body);

        var providedNames = new HashSet<string>();
        foreach (var field in staticBlock.Body.Fields)
            CheckStaticField(field, nonGenericInterfaceType, staticProperties, providedNames);

        foreach (var method in staticBlock.Body.Methods)
            CheckStaticMethod(method, nonGenericInterfaceType, staticProperties, providedNames);

        foreach (var missing in staticProperties.Keys.Except(providedNames).Order())
            _diagnostics.Error(
                staticBlock.InterfaceName,
                InternalCodes.StaticBlockMissingMember,
                $"Static block for interface '{nonGenericInterfaceType.Name}' is missing member '{missing}'."
            );

        return BindType(staticBlock, nonGenericInterfaceType);
    }

    /// <summary>
    ///     Reports a static member given a value twice. Two methods of one name are already the resolver's
    ///     ('already declared'), because a method is a variable in the block's scope - a field is not, so a
    ///     repeated field, or a method sharing a field's name, would otherwise emit both assignments and
    ///     leave the later one silently winning.
    /// </summary>
    private void CheckDuplicateStaticMembers(StaticBlockBody body)
    {
        var fieldNames = new HashSet<string>();
        foreach (var field in body.Fields.Where(field => !fieldNames.Add(field.Name.Text)))
            _diagnostics.Error(
                field,
                InternalCodes.DuplicateStaticMember,
                $"Static member '{field.Name.Text}' is already defined in this block.",
                "remove one of them"
            );

        foreach (var method in body.Methods.Where(method => fieldNames.Contains(method.Name.Text)))
            _diagnostics.Error(
                method,
                InternalCodes.DuplicateStaticMember,
                $"Static member '{method.Name.Text}' is already defined in this block.",
                "remove one of them"
            );
    }

    private void CheckStaticField(
        StaticFieldDeclaration field,
        InterfaceType interfaceType,
        Dictionary<string, ObjectProperty> staticProperties,
        HashSet<string> providedNames)
    {
        var name = field.Name.Text;
        providedNames.Add(name);

        if (!staticProperties.TryGetValue(name, out var declared))
        {
            _diagnostics.Error(
                field,
                InternalCodes.StaticBlockExtraMember,
                $"Interface '{interfaceType.Name}' does not declare a static member '{name}'."
            );

            MaybeVisit(field.ColonTypeClause);
            Visit(field.EqualsValueClause.Value);
            return;
        }

        var explicitType = field.ColonTypeClause != null ? Visit(field.ColonTypeClause) : null;
        if (explicitType != null)
            _semanticModel.TypeSolver.AddConstraint(explicitType, declared.ValueType, field.ColonTypeClause!.Type);

        var valueType = Check(field.EqualsValueClause.Value, explicitType ?? declared.ValueType);
        BindType(field, valueType);
    }

    private void CheckStaticMethod(
        FunctionDeclaration method,
        InterfaceType interfaceType,
        Dictionary<string, ObjectProperty> staticProperties,
        HashSet<string> providedNames)
    {
        var name = method.Name.Text;
        providedNames.Add(name);

        if (!staticProperties.TryGetValue(name, out var declared) || declared.ValueType is not FunctionType declaredSignature)
        {
            _diagnostics.Error(
                method,
                InternalCodes.StaticBlockExtraMember,
                $"Interface '{interfaceType.Name}' does not declare a static member '{name}'."
            );

            return;
        }

        BindType(method, declaredSignature);
        MaybeVisit(method.TypeParameters);

        var parameterCount = Math.Min(declaredSignature.ParameterTypes.Count, method.Parameters?.ParameterList.Count ?? 0);
        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = method.Parameters!.ParameterList[i];
            var explicitType = MaybeVisit(parameter.ColonTypeClause);
            var initializerType = MaybeVisit(parameter.EqualsValueClause);
            var type = declaredSignature.ParameterTypes[i];
            if (parameter.EqualsValueClause != null)
                _semanticModel.TypeSolver.AddConstraint(initializerType!, type, parameter.EqualsValueClause.Value);

            if (parameter.EqualsValueClause != null && Type.IsOptional(type))
                type = type.NonNullable();

            if (explicitType != null)
                _semanticModel.TypeSolver.AddConstraint(explicitType, type, parameter.ColonTypeClause!.Type);

            BindType(parameter, type);
        }

        var actualType = GetReturnType(method);
        _semanticModel.TypeSolver.AddConstraint(actualType, declaredSignature.ReturnType, method.ReturnType?.Type.LocationSpan ?? method.LocationSpan);
        if (method.ReturnType != null)
            BindType(method.ReturnType, declaredSignature.ReturnType);

        Visit(method.Body);
    }
}
