using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Generation.Serialization;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Luau;
using Loom.Luau.AST;
using Attribute = Loom.Core.Parsing.AST.Attribute;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Identifier = Loom.Luau.AST.Identifier;
using InterfaceType = Loom.Core.TypeChecking.Types.InterfaceType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

internal sealed class IntrinsicGlobalInvocationMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type __) => false;

    public bool Supports(SemanticModel semanticModel, Expression expression) =>
        expression is Parsing.AST.Identifier && semanticModel.GetSymbol(expression) is { IsIntrinsic: true };

    public bool IsInvocationOnlyMember(string memberName) =>
        memberName is "string" or "number" or "new_instance" or "get_service" or "type_is" or "get_metadata" or "has_attribute" or "wait"
            or "serialize_binary" or "deserialize_binary" or "serializer" or "serializer_of" or "diff_binary" or "apply_diff_binary";

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        var expanded = Expand(context, name, typeArguments, call);
        expression = expanded!;

        return expanded != null;
    }

    private static LuauExpression? Expand(MacroContext context, string name, TypeArguments? typeArguments, Call call) =>
        name switch
        {
            "get_service" => LuauFactory.LibraryCall("game", ["GetService"], [new StringLiteral(context.GetTextOfOnlyTypeArgument(typeArguments, name))], true),
            "new_instance" => LuauFactory.LibraryCall("Instance", ["new"], [new StringLiteral(context.GetTextOfOnlyTypeArgument(typeArguments, name))]),
            "string" => ExpandString(call),
            "number" => ExpandNumber(call),
            // the deprecated global 'wait' still exists in Roblox and is not this one, so the name is
            // resolved to task.wait rather than left to bind to whichever the runtime happens to have
            "wait" => LuauFactory.TaskCall("wait", call.Arguments),
            "type_is" => new BinaryOperator(new Call(new Identifier("typeof"), [call.Arguments[0]]), "==", call.Arguments[1]),
            "get_metadata" => ExpandGetMetadata(context, typeArguments, name),
            "has_attribute" => ExpandHasAttribute(context, typeArguments, name),
            "serializer_of" => ExpandSerializerOf(context, typeArguments, name, call),
            "serialize_binary" or "deserialize_binary" or "serializer" => ExpandCodec(context, typeArguments, name, call),
            "diff_binary" or "apply_diff_binary" => ExpandDiff(context, name, call),
            _ => null
        };

    private static LuauExpression ExpandString(Call call) =>
        TryFoldToString(call.Arguments[0], out var folded) ? folded : new Call(new Identifier("tostring"), call.Arguments);

    private static LuauExpression ExpandNumber(Call call) =>
        call.Arguments[0] is StringLiteral source
            ? LuauNumberFormat.TryParse(source.Value, out var parsed) ? new NumberLiteral(parsed) : new NilLiteral()
            : new Call(new Identifier("tonumber"), call.Arguments);

    private static LuauExpression ExpandGetMetadata(MacroContext context, TypeArguments? typeArguments, string name)
    {
        var matchedAttribute = FindMatchedAttribute(context, typeArguments, name, out var hadError);
        if (hadError || matchedAttribute == null)
            return new NilLiteral();

        return new Table(
            matchedAttribute.Arguments.ArgumentList
                .ConvertAll(argument => new TableInitializer(LuauFactory.Literal(context.SemanticModel.GetConstantValue(argument))))
        );
    }

    private static LuauExpression ExpandHasAttribute(MacroContext context, TypeArguments? typeArguments, string name)
    {
        var matchedAttribute = FindMatchedAttribute(context, typeArguments, name, out var hadError);

        return new BooleanLiteral(!hadError && matchedAttribute != null);
    }

    private static LuauExpression ExpandSerializerOf(MacroContext context, TypeArguments? typeArguments, string name, Call call)
    {
        if (typeArguments?.ArgumentsList.FirstOrDefault() is not { } mapArgument
            || context.SemanticModel.GetType(mapArgument) is not InterfaceType mapType)
        {
            context.Diagnostics.Error(context.Node, InternalCodes.InvalidTypeArguments, $"'{name}' requires a mapping interface as its first type argument.");

            return new NilLiteral();
        }

        if (!context.SemanticModel.SerializerMaps.Contains(mapType))
            context.SemanticModel.SerializerMaps.Add(mapType);

        return new Luau.AST.ElementAccess(new Identifier(SerializationEmitter.SerializerMapName(mapType.Name)), call.Arguments[0]);
    }

    private static LuauExpression ExpandCodec(MacroContext context, TypeArguments? typeArguments, string name, Call call)
    {
        // serialize_binary infers its interface from the argument's static type; deserialize_binary
        // has no value to infer from, so it takes the interface as a type argument.
        var isSerialize = name == "serialize_binary";
        var invocation = (Invocation)context.Node;
        Node? subject = isSerialize
            ? invocation.Arguments.ArgumentList.FirstOrDefault()
            : typeArguments?.ArgumentsList.FirstOrDefault();

        if (subject == null || !TryGetSerializableName(context, subject, name, out var interfaceName, out var declaringFile))
            return new NilLiteral();

        // 'serializer' hands back the codec value itself rather than calling through it.
        if (name == "serializer")
            return new Identifier(SerializationEmitter.SerializerName(interfaceName));

        return CallCodec(
            context,
            call,
            interfaceName,
            declaringFile,
            isSerialize ? "serialize" : "deserialize",
            isSerialize ? SerializationEmitter.SerializeName(interfaceName) : SerializationEmitter.DeserializeName(interfaceName)
        );
    }

    private static LuauExpression ExpandDiff(MacroContext context, string name, Call call)
    {
        // Both take the baseline as their first argument, so - unlike deserialize_binary - the
        // interface is always inferable from a value and neither needs a type argument.
        var isDiff = name == "diff_binary";
        var invocation = (Invocation)context.Node;
        var subject = invocation.Arguments.ArgumentList.FirstOrDefault();

        if (subject == null || !TryGetSerializableName(context, subject, name, out var interfaceName, out var declaringFile))
            return new NilLiteral();

        return CallCodec(
            context,
            call,
            interfaceName,
            declaringFile,
            isDiff ? "diff" : "apply_diff",
            isDiff ? SerializationEmitter.DiffName(interfaceName) : SerializationEmitter.ApplyDiffName(interfaceName)
        );
    }

    /// <summary>
    ///     Only the codec object crosses a module boundary, so an imported interface's calls go through
    ///     its <paramref name="codecMember" /> rather than through the function names that are local to
    ///     the declaring file.
    /// </summary>
    private static Call CallCodec(MacroContext context, Call call, string interfaceName, string declaringFile, string codecMember, string localFunction)
    {
        var isImported = declaringFile != context.SemanticModel.Tree.File.AbsolutePath;
        LuauExpression callee = isImported
            ? new Luau.AST.PropertyAccess(new Identifier(SerializationEmitter.SerializerName(interfaceName)), [codecMember])
            : new Identifier(localFunction);

        return new Call(callee, call.Arguments);
    }

    /// <summary>
    ///     Resolves the interface a serialization call targets and confirms the type checker built a
    ///     schema for it. Nothing in the signature constrains T to a serializable type, so an unmarked
    ///     interface has to be caught here rather than silently emitting a call to a function that was
    ///     never generated.
    /// </summary>
    private static bool TryGetSerializableName(MacroContext context, Node subject, string name, out string interfaceName, out string declaringFile)
    {
        interfaceName = "";
        declaringFile = "";
        var semanticModel = context.SemanticModel;
        var subjectType = semanticModel.GetType(subject);
        if (subjectType is not InterfaceType interfaceType)
        {
            context.Diagnostics.Error(
                context.Node,
                InternalCodes.NotSerializable,
                $"'{name}' requires a serializable interface, but was given '{subjectType}'."
            );

            return false;
        }

        var schemaEntry = semanticModel.SerializationSchemas.Keys.FirstOrDefault(s => s.Name == interfaceType.Name);
        if (schemaEntry == null)
        {
            context.Diagnostics.Error(
                context.Node,
                InternalCodes.NotSerializable,
                $"Interface '{interfaceType.Name}' is not serializable.",
                $"add the 'serializable' attribute to '{interfaceType.Name}'."
            );

            return false;
        }

        interfaceName = interfaceType.Name;
        declaringFile = schemaEntry.File.AbsolutePath;
        return true;
    }

    private static Attribute? FindMatchedAttribute(MacroContext context, TypeArguments? typeArguments, string name, out bool hadError)
    {
        hadError = false;
        var invocation = (Invocation)context.Node;
        var semanticModel = context.SemanticModel;

        var typeArgument = typeArguments?.ArgumentsList.FirstOrDefault();
        if (typeArgument == null || semanticModel.GetSymbol(typeArgument, SymbolKind.Interface) is not InterfaceSymbol interfaceSymbol)
        {
            context.Diagnostics.Error(invocation, InternalCodes.InvalidTypeArguments, $"'{name}' requires an interface type argument.");
            hadError = true;
            return null;
        }

        if (invocation.Arguments.ArgumentList is not [var memberNameExpression, var attributeExpression])
        {
            context.Diagnostics.CompilerError(invocation, $"'{name}' expects exactly 2 arguments.");
            hadError = true;
            return null;
        }

        IWithAttributes? target;
        if (memberNameExpression is Literal { Value: null })
        {
            target = interfaceSymbol.Declaration as InterfaceDeclaration;
        }
        else if (memberNameExpression is Literal { Value: string memberName })
        {
            var propertySymbol = interfaceSymbol.GetPropertyAtPath([memberName]);
            if (propertySymbol == null)
            {
                context.Diagnostics.Error(
                    memberNameExpression,
                    InternalCodes.UnknownMetadataMember,
                    $"Interface '{interfaceSymbol.Name}' has no member '{memberName}'."
                );

                hadError = true;
                return null;
            }

            target = propertySymbol.Declaration as IWithAttributes;
        }
        else
        {
            context.Diagnostics.Error(memberNameExpression, InternalCodes.UnknownMetadataMember, "'member_name' must be a string literal or 'none'.");
            hadError = true;
            return null;
        }

        var attributeSymbol = semanticModel.GetSymbol(attributeExpression);
        if (attributeSymbol == null)
        {
            context.Diagnostics.Error(
                attributeExpression,
                InternalCodes.InvalidMetadataAttributeReference,
                "'attribute' must be a direct reference to a decorator function."
            );

            hadError = true;
            return null;
        }

        return target?.Attributes?.AttributeList.Find(a => semanticModel.GetSymbol(a.Expression) == attributeSymbol);
    }

    private static bool TryFoldToString(LuauExpression argument, [MaybeNullWhen(false)] out StringLiteral folded)
    {
        switch (argument)
        {
            case StringLiteral stringLiteral:
                folded = stringLiteral;
                return true;
            case BooleanLiteral booleanLiteral:
                folded = new StringLiteral(booleanLiteral.Value ? "true" : "false");
                return true;
            case NilLiteral:
                folded = new StringLiteral("nil");
                return true;
            default:
                if (MacroContext.TryComputeConstantArithmetic(argument, out var number))
                {
                    folded = new StringLiteral(LuauNumberFormat.ToLuauString(number));
                    return true;
                }

                folded = null;
                return false;
        }
    }
}