using Loom.Core.TypeChecking.Serialization;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Serialization;

/// <summary>
///     Turns a <see cref="SerializationSchema" /> into its pair of top-level Luau functions. Every offset
///     the schema pins at compile time becomes a literal, so a fixed-size type allocates one buffer of a
///     constant width and writes it with straight-line calls - there is no runtime schema to walk.
/// </summary>
/// <remarks>
///     Buffer library members are reached through file-level constants rather than the <c>buffer</c>
///     global, since a serializer touches them once per field on paths that run per frame. Which members
///     a file needs falls out of <paramref name="bufferMembers" /> once every schema in it has been emitted.
/// </remarks>
internal sealed partial class SerializationEmitter(SerializationSchema schema, List<string> bufferMembers)
{
    private const string ValueParameter = "value";
    private const string BaselineParameter = "baseline";
    private const string DiffParameter = "diff";
    private const string SerializedParameter = "serialized";
    private const string BufferLocal = "b";
    private const string BufferLengthLocal = "b_len";
    private const string BlobsLocal = "blobs";
    private const string BlobIndexLocal = "blob_index";
    private const string OffsetLocal = "offset";
    private const string LoopLocal = "i";
    private const string SizeLocal = "size";

    public static string SerializeName(string interfaceName) => $"{interfaceName}_serialize_binary";
    public static string DeserializeName(string interfaceName) => $"{interfaceName}_deserialize_binary";
    public static string SerializerName(string interfaceName) => $"{interfaceName}_serializer";
    public static string SerializerMapName(string interfaceName) => $"{interfaceName}_serializer_map";
    public static string DiffName(string interfaceName) => $"{interfaceName}_diff_binary";
    public static string ApplyDiffName(string interfaceName) => $"{interfaceName}_apply_diff_binary";
    private static string BufferConstantName(string member) => $"buffer_{member}";
    private static string DiffWriteHelperName(string interfaceName) => $"{interfaceName}_diff_binary_write";
    private static string DiffAttemptHelperName(string interfaceName) => $"{interfaceName}_diff_binary_attempt";
    private static string DiffReadHelperName(string interfaceName) => $"{interfaceName}_apply_diff_binary_read";

    /// <summary>
    ///     Emits one table per mapping interface, keyed exactly as the interface is. Properties key by
    ///     name; an indexer whose key is a literal type - the shape an enum-keyed map takes - keys by
    ///     that literal, so <c>[Message["ShootGun"]]: ShootGunPacket</c> lands under the member's value.
    ///     A dispatch table typically merges several single-key interfaces through inheritance - each
    ///     contributing its own indexer - so every one of <see cref="InterfaceType.Indexers" /> is read,
    ///     not just the type's own.
    /// </summary>
    public static ConstVariable? EmitSerializerMap(
        InterfaceType mapType,
        Func<InterfaceType, string?> resolveSerializerName)
    {
        var initializers = new List<TableInitializer>();
        foreach (var property in mapType.Properties)
        {
            if (property.ValueType is not InterfaceType valueType || resolveSerializerName(valueType) is not { } serializerName) continue;
            initializers.Add(new PropertyTableInitializer(property.Name, new Identifier(serializerName)));
        }

        foreach (var indexer in mapType.Indexers)
        {
            if (indexer is { KeyType: LiteralType key, ValueType: InterfaceType indexedValue }
                && resolveSerializerName(indexedValue) is { } indexedSerializer)
                initializers.Add(new ComputedPropertyTableInitializer(LuauFactory.Literal(key.Value), new Identifier(indexedSerializer)));
        }

        return initializers.Count == 0
            ? null
            : new ConstVariable(SerializerMapName(mapType.Name), null, new Table(initializers));
    }

    /// <summary>
    ///     Bundles the pair into a value so it can be passed around, stored, or picked at runtime. The
    ///     named functions stay, so a call in the declaring file still goes direct - only crossing a
    ///     module boundary or holding the codec as a value pays the extra index.
    /// </summary>
    public ConstVariable EmitSerializerObject() =>
        new(
            SerializerName(schema.Interface.Name),
            LuauFactory.QualifyRuntimeType(new TypeName("Serializer", [new TypeName(schema.Interface.Name)])),
            new Table(
                [
                    new PropertyTableInitializer("serialize", new Identifier(SerializeName(schema.Interface.Name))),
                    new PropertyTableInitializer("deserialize", new Identifier(DeserializeName(schema.Interface.Name))),
                    new PropertyTableInitializer("diff", new Identifier(DiffName(schema.Interface.Name))),
                    new PropertyTableInitializer("apply_diff", new Identifier(ApplyDiffName(schema.Interface.Name)))
                ]
            )
        );

    /// <summary>Declares the hoisted constants for the members used across a file, in first-use order.</summary>
    public static List<LuauStatement> DeclareBufferConstants(IEnumerable<string> members) =>
        [.. members.Select(LuauStatement (member) => new ConstVariable(BufferConstantName(member), null, new PropertyAccess(new Identifier("buffer"), [member])))];

    private Identifier Buffer(string member)
    {
        if (!bufferMembers.Contains(member))
            bufferMembers.Add(member);

        return new Identifier(BufferConstantName(member));
    }

    private Call BufferCall(string member, List<LuauExpression> arguments) => new(Buffer(member), arguments);

    /// <summary>
    ///     Unions whose tag is already resolved, by path, naming the local holding it. The prologue puts
    ///     one there for every top-level union ahead of the allocation that depends on it, and a diff puts
    ///     the current value's tag there for the length of the branch that resends the union - both leave a
    ///     local a later write can simply read instead of running the same comparison chain again. A union
    ///     inside a collection has one tag per entry, so it cannot be hoisted and is resolved in the loop.
    /// </summary>
    private readonly Dictionary<string, string> _resolvedTags = [];
}
