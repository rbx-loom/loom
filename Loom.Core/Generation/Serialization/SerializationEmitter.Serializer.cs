using Loom.Core.TypeChecking.Serialization;
using Loom.Luau;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Serialization;

/// <summary>Writing: the sentinel and union prologue and per-field writes.</summary>
internal sealed partial class SerializationEmitter
{
    private static readonly List<string> _cframeSentinels = ["CFrame.identity"];

    /// <summary>
    ///     Collections whose count is already in a local, by the field counted. Luau has no length operator
    ///     for a keyed table, so a map's count is a loop either way - but the writer's prefix needs the same
    ///     number the sizing already walked for, and so does an array's, which reads its length three times
    ///     over otherwise: once for the prefix, once to size its entries' bit block, once to bound the loop.
    /// </summary>
    /// <remarks>
    ///     An entry means the local is reachable from wherever the emitter currently is, which is what makes
    ///     reading it back safe. Whoever put it there is who knows that: the size pass registers only what it
    ///     declared at the function's root, which every block below can see, and a caller that bound a count
    ///     itself registers it for exactly as long as the statements it encloses are being emitted.
    /// </remarks>
    private readonly Dictionary<SerializationField, string> _boundCounts = [];
    private List<LuauStatement>? _measureRoot;

    public Function EmitSerializer()
    {
        _locals.Clear();
        _boundCounts.Clear();
        var body = new List<LuauStatement>();
        if (schema.HasBlobs)
            body.Add(new ConstVariable(BlobsLocal, null, Table.Empty));

        EmitSentinelPrologue(body);

        if (!schema.IsEmpty)
            body.Add(new ConstVariable(BufferLocal, null, BufferCall("create", [EmitSizeComputation(body)])));

        var cursor = new Cursor(schema.HeaderBytes);
        foreach (var serializationField in schema.Fields)
            EmitWrite(serializationField, new Identifier(ValueParameter), cursor, body);

        body.Add(new Return(BuildSerializedTable()));
        return new Function(
            SerializeName(schema.Interface.Name),
            null,
            [new Parameter(ValueParameter, new TypeName(schema.Interface.Name))],
            LuauFactory.QualifyRuntimeType(new TypeName("Serialized")),
            new Chunk(body)
        );
    }

    /// <summary>
    ///     Binds each sentinelled field's value and resolves which sentinel it matched, ahead of the
    ///     allocation that depends on the answer. A match writes no components at all, so the width is
    ///     not known until the comparison chain has run.
    /// </summary>
    private void EmitSentinelPrologue(List<LuauStatement> body)
    {
        foreach (var serializationField in schema.Fields)
        {
            if (serializationField is UnionField unionField)
            {
                EmitUnionTag(unionField, Access(new Identifier(ValueParameter), unionField.Path), body);
                _resolvedTags[unionField.Path] = UnionTagLocal(unionField.Path);
                continue;
            }

            if (SentinelNamesOf(serializationField) is not { Count: > 0 } sentinels)
                continue;

            var valueLocal = SentinelValueLocal(serializationField.Path);
            body.Add(new ConstVariable(valueLocal, null, Access(new Identifier(ValueParameter), serializationField.Path)));
            body.Add(new LocalVariable(SentinelIndexLocal(serializationField.Path), null, _zero));

            var chain = new IfChain(body);
            for (var index = 0; index < sentinels.Count; index++)
            {
                var condition = new BinaryOperator(new Identifier(valueLocal), "==", new Identifier(sentinels[index]));
                var assign = new Chunk(
                    [new ExpressionStatement(new BinaryOperator(new Identifier(SentinelIndexLocal(serializationField.Path)), "=", new NumberLiteral(index + 1)))]
                );

                chain.Add(condition, assign);
            }
        }
    }

    /// <summary>Luau-side sentinel constants for a field, or null when it is not sentinelled.</summary>
    private static IReadOnlyList<string>? SentinelNamesOf(SerializationField serializationField) =>
        serializationField switch
        {
            DatatypeField { UseSentinels: true } datatypeField => datatypeField.Datatype.Sentinels,
            CFrameField { UseSentinels: true } => _cframeSentinels,
            _ => null
        };

    private static string SentinelValueLocal(string path) => LeafName(path) + "_value";
    private static string SentinelIndexLocal(string path) => LeafName(path) + "_sentinel";
    private static string UnionTagLocal(string path) => LeafName(path) + "_tag";

    /// <summary>
    ///     Names the local holding this union's tag, resolving it first when nothing has already. Every
    ///     caller wants the same thing - a number saying which variant is live - so whoever needed it first
    ///     is who pays for it, and the comparison chain runs once however many places read the answer.
    /// </summary>
    private Identifier UnionTag(UnionField unionField, LuauExpression value, List<LuauStatement> body)
    {
        if (_resolvedTags.TryGetValue(unionField.Path, out var resolved))
            return new Identifier(resolved);

        EmitUnionTag(unionField, value, body);
        return new Identifier(UnionTagLocal(unionField.Path));
    }

    /// <summary>
    ///     Resolves which variant a union value is, ahead of the allocation that depends on it. Index zero
    ///     is the fallback rather than a reserved escape - the type guarantees some variant matches, so
    ///     the first needs no test of its own.
    /// </summary>
    private static void EmitUnionTag(UnionField unionField, LuauExpression value, List<LuauStatement> body)
    {
        var tagLocal = UnionTagLocal(unionField.Path);

        var subject = value;
        if (subject is not Identifier)
        {
            subject = new Identifier(SentinelValueLocal(unionField.Path));
            body.Add(new ConstVariable(((Identifier)subject).Name, null, value));
        }

        body.Add(new LocalVariable(tagLocal, null, _zero));

        var chain = new IfChain(body);
        for (var index = 1; index < unionField.Variants.Count; index++)
        {
            var condition = VariantCondition(unionField, subject, index);
            chain.Add(condition, new Chunk([new ExpressionStatement(new BinaryOperator(new Identifier(tagLocal), "=", new NumberLiteral(index)))]));
        }
    }

    /// <summary>Test that identifies a variant, by literal value, runtime kind, or shared discriminant.</summary>
    private static BinaryOperator VariantCondition(UnionField unionField, LuauExpression value, int index)
    {
        var discriminant = unionField.Variants[index].Discriminant;
        return unionField.Discrimination switch
        {
            UnionDiscrimination.LiteralValue => new BinaryOperator(value, "==", LuauFactory.Literal(discriminant)),
            UnionDiscrimination.RuntimeKind => new BinaryOperator(
                new Call(new Identifier("typeof"), [value]),
                "==",
                new StringLiteral((string)discriminant!)
            ),
            _ => new BinaryOperator(new PropertyAccess(value, [unionField.DiscriminantName!]), "==", LuauFactory.Literal(discriminant))
        };
    }

    private static int VariantBytes(SerializationVariant variant) => variant.Fields.Sum(f => f.BodyBytes ?? 0);

    /// <summary>
    ///     A variant's width, or null when it writes nothing. Variable-width fields inside a variant still
    ///     need measuring - a string variant sized only by its fixed part would under-allocate and then
    ///     overrun the buffer it was given.
    /// </summary>
    private static LuauExpression? VariantSizeExpression(UnionField unionField, SerializationVariant variant, LuauExpression value)
    {
        var constant = 0;
        var measured = new List<LuauExpression>();
        foreach (var (variantField, variantValue) in ChildrenOf(unionField, variant, value))
        {
            if (variantField.BodyBytes is { } fixedBytes)
            {
                constant += fixedBytes;
                continue;
            }

            if (InlineContribution(variantField, variantValue) is { } contribution)
                measured.Add(contribution);
        }

        if (measured.Count == 0)
            return constant > 0 ? new NumberLiteral(constant) : null;

        var total = measured.Aggregate(Add);
        return constant > 0 ? Add(new NumberLiteral(constant), total) : total;
    }
    /// <summary>Bytes a sentinelled field writes when nothing matched and its components go out in full.</summary>
    private static int SentinelComponentBytes(SerializationField serializationField) =>
        serializationField switch
        {
            DatatypeField datatypeField => datatypeField.Datatype.Components.Count * datatypeField.NumberType.ByteCount(),

            CFrameField cframeField => cframeField.ComponentCount * cframeField.NumberType.ByteCount()
                + (cframeField.Encoding == CFrameEncoding.Compressed ? sizeof(uint) : 0),
            _ => 0
        };

    /// <summary>
    ///     Reserves the bit block a collection's entries share, returning its origin in bits, or null when
    ///     the entries need no bits at all. The block is claimed before any body so the bodies keep their
    ///     byte alignment.
    /// </summary>
    /// <remarks>
    ///     The origin scales the position rather than indexing at it, so the cursor is settled first: a
    ///     folded position is a sum, and multiplying it whole is not what writing it out would mean.
    /// </remarks>
    private Identifier? ReserveElementBits(int bitsPerElement, string leaf, LuauExpression count, Cursor cursor, List<LuauStatement> body)
    {
        if (bitsPerElement == 0)
            return null;

        cursor.Flush(body);
        var origin = ReserveLocal(leaf + "_bits");
        body.Add(new ConstVariable(origin, null, Multiply(cursor.Position, new NumberLiteral(8))));

        cursor.AdvanceBy(body, ElementBitBlockSize(bitsPerElement, count));
        return new Identifier(origin);
    }

    /// <summary>
    ///     Points the cursor at one entry's slice of the shared bit block, returning the undo. Bit offsets
    ///     restart per entry; byte offsets keep running, since bodies follow one another.
    /// </summary>
    private static Action EnterElement(Cursor cursor, LuauExpression? bitBase, int bitsPerElement, string loopLocal)
    {
        if (bitBase == null)
            return () =>
            {
            };

        var previousBase = cursor.BitBase;
        var previousOffset = cursor.BitOffset;

        cursor.BitBase = Add(bitBase, Multiply(new Parenthesized(Subtract(new Identifier(loopLocal), _one)), new NumberLiteral(bitsPerElement)));
        cursor.BitOffset = 0;

        return () =>
        {
            cursor.BitBase = previousBase;
            cursor.BitOffset = previousOffset;
        };
    }

    /// <summary>Whole bytes the shared bit block occupies, or zero when the entries need no bits.</summary>
    private static LuauExpression ElementBitBlockSize(int bitsPerElement, LuauExpression count) =>
        bitsPerElement == 0
            ? _zero
            : FloorDivide(
                new Parenthesized(Add(Multiply(count, new NumberLiteral(bitsPerElement)), new NumberLiteral(7))),
                new NumberLiteral(8)
            );

    private static BinaryOperator FloorDivide(LuauExpression left, LuauExpression right) => new(left, "//", right);

    private static UnaryOperator Length(LuauExpression value) => new("#", value);

    private static BinaryOperator IsPresent(LuauExpression value) => new(value, "~=", new NilLiteral());

    /// <summary>
    ///     An all-zero-width type sends no buffer, and a type with no blob fields sends no blobs array.
    ///     Both are statically known here, so the absent field is simply omitted from the table.
    /// </summary>
    private Table BuildSerializedTable()
    {
        var initializers = new List<TableInitializer>();
        if (!schema.IsEmpty)
            initializers.Add(new PropertyTableInitializer("buffer", new Identifier(BufferLocal)));

        if (schema.HasBlobs)
            initializers.Add(new PropertyTableInitializer("blobs", new Identifier(BlobsLocal)));

        return new Table(initializers);
    }

    private void EmitWrite(SerializationField serializationField, LuauExpression source, Cursor cursor, List<LuauStatement> body) =>
        EmitValueWrite(serializationField, Access(source, serializationField.Path), cursor, body);

    /// <summary>
    ///     Writes a field given the expression holding its value, rather than resolving it from a path.
    ///     Array elements are reached by index, so they have no path of their own and reuse this directly.
    /// </summary>
    private void EmitValueWrite(SerializationField serializationField, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        switch (serializationField)
        {
            case ConstantField:
                return;

            case BoolField:
                body.Add(new ExpressionStatement(WriteBits(cursor, 1, new IfExpression(value, _one, [], _zero))));
                return;

            case NumberField numberField:
                WriteNumber(cursor, numberField.NumberType, value, body);
                return;

            case RangedNumberField ranged:
                EmitRangedWrite(ranged, value, cursor, body);
                return;

            case BlobField:
                body.Add(new ExpressionStatement(LuauFactory.TableCall("insert", [new Identifier(BlobsLocal), value])));
                return;

            case OptionalField optionalField:
                EmitOptionalWrite(optionalField, value, cursor, body);
                return;

            case MapField mapField:
                EmitMapWrite(mapField, value, cursor, body);
                return;

            case ArrayField arrayField:
                EmitArrayWrite(arrayField, value, cursor, body);
                return;

            case StringField stringField:
                EmitStringWrite(stringField, value, cursor, body);
                return;

            case DatatypeField { UseSentinels: false } datatypeField:
                EmitDatatypeWrite(datatypeField, value, cursor, body);
                return;

            case CFrameField { UseSentinels: false } cframeField:
                EmitCFrameWrite(cframeField, value, cursor, body);
                return;

            case DatatypeField or CFrameField:
                EmitSentinelledWrite(serializationField, cursor, body);
                return;

            case TupleField tupleField:
                foreach (var (element, elementValue) in ChildrenOf(tupleField, value))
                    EmitValueWrite(element, elementValue, cursor, body);

                return;

            case UnionField unionField:
                EmitUnionWrite(unionField, value, cursor, body);
                return;
        }
    }

    private void EmitRangedWrite(RangedNumberField ranged, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        var scaled = value;
        if (!ranged.Minimum.Equals(0d))
            scaled = new Parenthesized(Subtract(scaled, new NumberLiteral(ranged.Minimum)));

        if (!ranged.Step.Equals(1d))
            scaled = Divide(scaled, new NumberLiteral(ranged.Step));

        body.Add(new ExpressionStatement(WriteBits(cursor, ranged.HeaderBits, LuauFactory.MathCall("round", [scaled]))));
    }

    private void EmitOptionalWrite(OptionalField optionalField, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        body.Add(new ExpressionStatement(WriteBits(cursor, 1, new IfExpression(IsPresent(value), _one, [], _zero))));

        cursor.GoDynamic(body);
        cursor.Flush(body);

        var present = new List<LuauStatement>();
        foreach (var (inner, innerValue) in ChildrenOf(optionalField, value))
            EmitValueWrite(inner, innerValue, cursor, present);

        cursor.Flush(present);
        body.Add(new IfStatement(IsPresent(value), new Chunk(present), [], null));
    }

    private void EmitMapWrite(MapField mapField, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        var leaf = LeafName(mapField.Path);
        var count = MapCount(mapField, leaf, value, body);

        WriteNumber(cursor, mapField.LengthType, count, body);
        if (NeedsBufferSpace(mapField.Key) || NeedsBufferSpace(mapField.Value))
            cursor.GoDynamic(body);

        var pairBits = ReserveElementBits(mapField.EntryBits, leaf, count, cursor, body);
        var index = ReserveLocal(leaf + "_index");
        if (pairBits != null)
            body.Add(new LocalVariable(index, null, _one));

        cursor.Flush(body);

        using var pairScope = LoopScope();
        var keyLocal = ReserveLocal(LeafName(mapField.Key.Path));
        var valueLocal = ReserveLocal(LeafName(mapField.Value.Path));
        var pairBody = new List<LuauStatement>();

        var restorePair = EnterElement(cursor, pairBits, mapField.EntryBits, index);
        EmitValueWrite(mapField.Key, new Identifier(keyLocal), cursor, pairBody);
        EmitValueWrite(mapField.Value, new Identifier(valueLocal), cursor, pairBody);
        restorePair();
        cursor.Flush(pairBody);

        if (pairBits != null)
            pairBody.Add(new ExpressionStatement(new BinaryOperator(new Identifier(index), "+=", _one)));

        body.Add(new ForStatement([keyLocal, valueLocal], value, new Chunk(pairBody)));
    }

    private void EmitArrayWrite(ArrayField arrayField, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        var leaf = LeafName(arrayField.Path);
        var count = ArrayCount(arrayField, leaf, value, body);
        WriteNumber(cursor, arrayField.LengthType, count, body);
        if (NeedsBufferSpace(arrayField.Element))
            cursor.GoDynamic(body);

        var bitBase = ReserveElementBits(arrayField.Element.HeaderBits, leaf, count, cursor, body);
        cursor.Flush(body);

        using var elementScope = LoopScope();
        var loop = ReserveLocal(LoopLocal);
        var elementBody = new List<LuauStatement>();
        var element = new Identifier(ReserveLocal(LeafName(arrayField.Element.Path)));
        elementBody.Add(new ConstVariable(element.Name, null, new ElementAccess(value, new Identifier(loop))));

        var restore = EnterElement(cursor, bitBase, arrayField.Element.HeaderBits, loop);
        EmitValueWrite(arrayField.Element, element, cursor, elementBody);
        restore();
        cursor.Flush(elementBody);

        body.Add(new NumericForStatement(loop, _one, count, null, new Chunk(elementBody)));
    }

    private void EmitStringWrite(StringField stringField, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        var text = BindIfReused(value, 2, LeafName(stringField.Path), body);
        var length = new Identifier(ReserveLocal(LeafName(stringField.Path) + "_length"));
        body.Add(new ConstVariable(length.Name, null, Length(text)));

        WriteNumber(cursor, stringField.LengthType, length, body);
        cursor.GoDynamic(body);
        body.Add(new ExpressionStatement(BufferCall("writestring", [new Identifier(BufferLocal), cursor.Position, text])));
        cursor.AdvanceBy(body, length);
    }

    private void EmitDatatypeWrite(DatatypeField datatypeField, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        var bound = BindIfReused(value, datatypeField.Datatype.Components.Count, LeafName(datatypeField.Path), body);
        foreach (var component in datatypeField.Datatype.Components)
            WriteNumber(cursor, datatypeField.NumberType, Access(bound, component), body);
    }

    private void EmitSentinelledWrite(SerializationField serializationField, Cursor cursor, List<LuauStatement> body)
    {
        var sentinels = SentinelNamesOf(serializationField)!;
        var indexLocal = new Identifier(SentinelIndexLocal(serializationField.Path));
        var bound = new Identifier(SentinelValueLocal(serializationField.Path));
        body.Add(new ExpressionStatement(WriteBits(cursor, BitWidth.ForStateCount(sentinels.Count + 1), indexLocal)));

        cursor.GoDynamic(body);
        cursor.Flush(body);

        var components = new List<LuauStatement>();
        if (serializationField is DatatypeField datatype)
            foreach (var component in datatype.Datatype.Components)
                WriteNumber(cursor, datatype.NumberType, Access(bound, component), components);
        else
            EmitCFrameWrite((CFrameField)serializationField, bound, cursor, components);

        cursor.Flush(components);
        body.Add(new IfStatement(new BinaryOperator(indexLocal, "==", _zero), new Chunk(components), [], null));
    }

    private void EmitUnionWrite(UnionField unionField, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        var tag = UnionTag(unionField, value, body);
        body.Add(new ExpressionStatement(WriteBits(cursor, unionField.TagBits, tag)));
        cursor.GoDynamic(body);
        cursor.Flush(body);

        var startBit = cursor.BitOffset;
        var widestBit = startBit;
        var chain = new IfChain(body);
        for (var index = 0; index < unionField.Variants.Count; index++)
        {
            cursor.BitOffset = startBit;
            var variantBody = new List<LuauStatement>();
            foreach (var (variantField, variantValue) in ChildrenOf(unionField, unionField.Variants[index], value))
                EmitValueWrite(variantField, variantValue, cursor, variantBody);

            widestBit = Math.Max(widestBit, cursor.BitOffset);
            cursor.Flush(variantBody);

            if (variantBody.Count == 0)
                continue;

            chain.Add(new BinaryOperator(tag, "==", new NumberLiteral(index)), new Chunk(variantBody));
        }

        cursor.BitOffset = widestBit;
    }

    private void EmitCFrameWrite(CFrameField cframeField, LuauExpression value, Cursor cursor, List<LuauStatement> body)
    {
        foreach (var component in _cFramePositionComponents)
            WriteNumber(cursor, cframeField.NumberType, Access(value, component), body);

        var quaternion = LuauFactory.RuntimeLibraryCall(["cframe_to_quaternion"], [value]);
        if (cframeField.Encoding == CFrameEncoding.Compressed)
        {
            if (cframeField.UseSentinels)
                WriteNumber(cursor, NumberType.U32, PackQuaternion(quaternion), body);
            else
                body.Add(new ExpressionStatement(WriteBits(cursor, CFrameEncodingExtensions.CompressedRotationBits, PackQuaternion(quaternion))));

            return;
        }

        body.Add(new MultiConstVariable(_quaternionLocals, quaternion));
        foreach (var local in _quaternionLocals)
            WriteNumber(cursor, cframeField.NumberType, new Identifier(local), body);
    }

    private static Call PackQuaternion(LuauExpression quaternion) => LuauFactory.RuntimeLibraryCall(["pack_quaternion"], [quaternion]);

    private void WriteNumber(Cursor cursor, NumberType numberType, LuauExpression value, List<LuauStatement> body)
    {
        body.Add(new ExpressionStatement(BufferCall("write" + numberType.BufferSuffix(), [new Identifier(BufferLocal), cursor.Position, value])));
        cursor.Advance(numberType.ByteCount());
    }

    private Call WriteBits(Cursor cursor, int bitCount, LuauExpression value)
    {
        var call = BufferCall(
            "writebits",
            [new Identifier(BufferLocal), cursor.BitPosition, new NumberLiteral(bitCount), value]
        );

        cursor.BitOffset += bitCount;
        return call;
    }
}
