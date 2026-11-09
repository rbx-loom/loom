using Loom.Core.TypeChecking.Serialization;
using Loom.Luau;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Serialization;

/// <summary>Reading: bounds and tag validation, per-field reads, and rebuilding the value.</summary>
internal sealed partial class SerializationEmitter
{
    public Function EmitDeserializer()
    {
        _locals.Clear();
        _readsBufferLength = false;
        var body = new List<LuauStatement>();
        var readCursor = new Cursor(schema.HeaderBytes);
        var reads = new List<LuauStatement>();
        var initializers = schema.Fields
            .Select(TableInitializer (serializationField) => new PropertyTableInitializer(
                    LeafName(serializationField.Path),
                    EmitRead(serializationField, readCursor, reads)
                )
            )
            .ToList();

        if (!schema.IsEmpty)
        {
            body.Add(new ConstVariable(BufferLocal, null, new PropertyAccess(new Identifier(SerializedParameter), ["buffer"])));
            body.AddRange(EmitTruncationGuard(MinimumByteCount()));
        }

        if (schema.HasBlobs)
        {
            body.Add(new ConstVariable(BlobsLocal, null, new PropertyAccess(new Identifier(SerializedParameter), ["blobs"])));
            body.Add(
                new IfStatement(
                    new BinaryOperator(new Identifier(BlobsLocal), "==", new NilLiteral()),
                    new Chunk([new Return(BuildErrorTable("missing_blob", null, null))]),
                    [],
                    null
                )
            );

            body.Add(new LocalVariable(BlobIndexLocal, null, _one));
        }

        body.AddRange(reads);
        body.Add(
            new Return(new Table([new PropertyTableInitializer("ok", new BooleanLiteral(true)), new PropertyTableInitializer("value", new Table(initializers))]))
        );

        return new Function(
            DeserializeName(schema.Interface.Name),
            null,
            [new Parameter(SerializedParameter, LuauFactory.QualifyRuntimeType(new TypeName("Serialized")))],
            LuauFactory.QualifyRuntimeType(
                new TypeName("Result", [new TypeName(schema.Interface.Name), LuauFactory.QualifyRuntimeType(new TypeName("DeserializeError"))])
            ),
            new Chunk(body)
        );
    }

    /// <summary>
    ///     The buffer's length, named by the guard that already had to measure it. A buffer cannot change
    ///     size, so every bounds check past that point is a comparison against a local rather than another
    ///     call into the library - one per variable-width field, and one per element of an array of them.
    /// </summary>
    private Identifier BufferLength()
    {
        _readsBufferLength = true;
        return new Identifier(BufferLengthLocal);
    }

    private bool _readsBufferLength;

    /// <summary>
    ///     The minimum width is known, so one up-front check covers every fixed read that follows - no
    ///     pcall, and a specific error rather than "buffer access out of bounds". The length is bound only
    ///     when something downstream went on to ask for it, so a schema whose reads are all fixed-width is
    ///     not left carrying a local nobody reads.
    /// </summary>
    /// <remarks>
    ///     Emitted after the reads it guards, since only they can say whether the length is wanted again.
    /// </remarks>
    private List<LuauStatement> EmitTruncationGuard(int minimumBytes)
    {
        var missing = new BinaryOperator(new Identifier(BufferLocal), "==", new NilLiteral());
        var truncated = new Chunk([new Return(BuildErrorTable("truncated", null, 0))]);
        if (!_readsBufferLength)
            return
            [
                new IfStatement(
                    new BinaryOperator(missing, "or", new BinaryOperator(BufferCall("len", [new Identifier(BufferLocal)]), "<", new NumberLiteral(minimumBytes))),
                    truncated,
                    [],
                    null
                )
            ];

        return
        [
            new ConstVariable(
                BufferLengthLocal,
                null,
                new IfExpression(IsPresent(new Identifier(BufferLocal)), BufferCall("len", [new Identifier(BufferLocal)]), [], _zero)
            ),
            new IfStatement(
                new BinaryOperator(missing, "or", new BinaryOperator(new Identifier(BufferLengthLocal), "<", new NumberLiteral(minimumBytes))),
                truncated,
                [],
                null
            )
        ];
    }

    /// <summary>
    ///     Smallest buffer a valid payload can occupy - every fixed field at full width plus, for a
    ///     variable field, its unavoidable part. A string contributes its length prefix, since an empty
    ///     string still writes one. Enough to cover every read up to the first variable segment, which
    ///     then bounds-checks itself.
    /// </summary>
    private int MinimumByteCount() =>
        schema.HeaderBytes
        + schema.Fields.Sum(f => f.BodyBytes ?? MinimumOf(f));

    /// <summary>Unavoidable width of a variable field: a length prefix is written even when empty.</summary>
    private static int MinimumOf(SerializationField serializationField) =>
        serializationField switch
        {
            StringField stringField => stringField.LengthType.ByteCount(),
            ArrayField arrayField => arrayField.LengthType.ByteCount(),
            MapField mapField => mapField.LengthType.ByteCount(),
            _ => 0
        };

    private static Table BuildErrorTable(string kind, string? fieldPath, int? offset)
    {
        var initializers = new List<TableInitializer> { new PropertyTableInitializer("kind", new StringLiteral(kind)) };
        if (fieldPath != null)
            initializers.Add(new PropertyTableInitializer("field", new StringLiteral(fieldPath)));

        if (offset != null)
            initializers.Add(new PropertyTableInitializer("offset", new NumberLiteral(offset.Value)));

        return new Table([new PropertyTableInitializer("ok", new BooleanLiteral(false)), new PropertyTableInitializer("error", new Table(initializers))]);
    }

    /// <summary>
    ///     Returns the table initializers reconstructing this field, appending any statements it needs to
    ///     <paramref name="statements" />. Leaf reads are expressions, so most fields need no statement.
    /// </summary>
    /// <summary>
    ///     Reads one field, returning the expression that reconstructs it. A flattened struct builds its
    ///     own table from its parts, so nesting is structural rather than something a later pass has to
    ///     reassemble from dotted names.
    /// </summary>
    private LuauExpression EmitRead(SerializationField serializationField, Cursor cursor, List<LuauStatement> statements) =>
        serializationField switch
        {
            ConstantField constant => LuauFactory.Literal(constant.Value),
            BoolField => new BinaryOperator(ReadBits(cursor, 1), "==", _one),
            NumberField numberField => ReadNumber(cursor, numberField.NumberType, statements, LeafName(numberField.Path)),
            RangedNumberField ranged => EmitRangedRead(ranged, cursor),
            BlobField blobField => EmitBlobRead(blobField, statements),
            OptionalField optionalField => EmitOptionalRead(optionalField, cursor, statements),
            ArrayField arrayField => EmitArrayRead(arrayField, cursor, statements),
            MapField mapField => EmitMapRead(mapField, cursor, statements),
            StringField stringField => EmitStringRead(stringField, cursor, statements),
            DatatypeField { UseSentinels: false } datatypeField => new Call(
                new Identifier(datatypeField.Datatype.Constructor),
                [.. datatypeField.Datatype.Components.Select(component => ReadNumber(cursor, datatypeField.NumberType, statements, ComponentName(datatypeField.Path, component)))]
            ),
            CFrameField { UseSentinels: false } cframeField => EmitCFrameRead(cframeField, cursor, statements),
            DatatypeField or CFrameField => EmitSentinelRead(serializationField, cursor, statements),
            TupleField tupleField => EmitStructRead(tupleField, cursor, statements),
            UnionField unionField => EmitUnionRead(unionField, cursor, statements),
            _ => new NilLiteral()
        };

    /// <summary>Rebuilds a flattened struct as the table its properties belonged to.</summary>
    private Table EmitStructRead(TupleField tupleField, Cursor cursor, List<LuauStatement> statements)
    {
        var initializers = tupleField.Elements
            .Select(TableInitializer (element) => new PropertyTableInitializer(LeafName(element.Path), EmitRead(element, cursor, statements)))
            .ToList();

        return new Table(initializers);
    }

    private LuauExpression EmitRangedRead(RangedNumberField ranged, Cursor cursor)
    {
        LuauExpression decoded = ReadBits(cursor, ranged.HeaderBits);
        if (!ranged.Step.Equals(1d))
            decoded = Multiply(decoded, new NumberLiteral(ranged.Step));

        return ranged.Minimum.Equals(0d) ? decoded : Add(new NumberLiteral(ranged.Minimum), decoded);
    }

    /// <summary>
    ///     Blobs are consumed positionally. The count is checked and the value type verified where it can
    ///     be - a wrong-typed blob from a hostile client would otherwise violate the declared type.
    /// </summary>
    private Identifier EmitBlobRead(BlobField blobField, List<LuauStatement> statements)
    {
        var slot = new ElementAccess(new Identifier(BlobsLocal), new Identifier(BlobIndexLocal));
        var local = ReserveLocal(LeafName(blobField.Path) + "_blob");
        statements.Add(new ConstVariable(local, null, slot));
        statements.Add(new ExpressionStatement(new BinaryOperator(new Identifier(BlobIndexLocal), "+=", _one)));
        statements.Add(
            new IfStatement(
                new BinaryOperator(new Identifier(local), "==", new NilLiteral()),
                new Chunk([new Return(BuildErrorTable("missing_blob", blobField.Path, null))]),
                [],
                null
            )
        );

        if (blobField.TypeofCheck == null)
            return new Identifier(local);

        LuauExpression condition = new BinaryOperator(
            new Call(new Identifier("typeof"), [new Identifier(local)]),
            "~=",
            new StringLiteral(blobField.TypeofCheck)
        );

        if (blobField.InstanceClass != null)
            condition = new BinaryOperator(
                condition,
                "or",
                new UnaryOperator("not ", new Call(new PropertyAccess(new Identifier(local), ["IsA"]), [new StringLiteral(blobField.InstanceClass)], true))
            );

        statements.Add(
            new IfStatement(
                condition,
                new Chunk([new Return(BuildErrorTable("invalid_blob", blobField.Path, null))]),
                [],
                null
            )
        );

        return new Identifier(local);
    }

    /// <summary>
    ///     Reads a count-prefixed run of pairs back into a table. Order is not preserved and does not need
    ///     to be - a map is unordered, so any order rebuilds the same value.
    /// </summary>
    private Identifier EmitMapRead(MapField mapField, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = ReserveLocal(LeafName(mapField.Path));
        var count = BindRead(ReadNumber(cursor, mapField.LengthType, statements, leaf + "_count"), leaf + "_count", statements);
        if (NeedsBufferSpace(mapField.Key) || NeedsBufferSpace(mapField.Value))
            cursor.GoDynamic(statements);

        var pairBits = ReserveElementBits(mapField.EntryBits, leaf, count, cursor, statements);
        statements.Add(new ConstVariable(leaf, null, Table.Empty));
        cursor.Flush(statements);

        using var scope = LoopScope();
        var loop = ReserveLocal(LoopLocal);
        var pairBody = new List<LuauStatement>();
        var restorePair = EnterElement(cursor, pairBits, mapField.EntryBits, loop);
        var key = EmitRead(mapField.Key, cursor, pairBody);
        var value = EmitRead(mapField.Value, cursor, pairBody);
        restorePair();
        cursor.Flush(pairBody);

        pairBody.Add(new ExpressionStatement(new BinaryOperator(new ElementAccess(new Identifier(leaf), key), "=", value)));
        statements.Add(new NumericForStatement(loop, _one, count, null, new Chunk(pairBody)));

        return new Identifier(leaf);
    }

    private Identifier EmitArrayRead(ArrayField arrayField, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = ReserveLocal(LeafName(arrayField.Path));
        var count = BindRead(ReadNumber(cursor, arrayField.LengthType, statements, leaf + "_count"), leaf + "_count", statements);
        if (NeedsBufferSpace(arrayField.Element))
            cursor.GoDynamic(statements);

        if (arrayField.Element.BodyBytes is > 0 and { } elementBytes)
            statements.Add(
                new IfStatement(
                    new BinaryOperator(
                        BufferLength(),
                        "<",
                        Add(cursor.Position, Multiply(count, new NumberLiteral(elementBytes)))
                    ),
                    new Chunk([new Return(BuildErrorTable("invalid_length", arrayField.Path, null))]),
                    [],
                    null
                )
            );

        var bitBase = ReserveElementBits(arrayField.Element.HeaderBits, leaf, count, cursor, statements);

        statements.Add(new ConstVariable(leaf, null, Table.Empty));
        cursor.Flush(statements);

        using var scope = LoopScope();
        var loop = ReserveLocal(LoopLocal);
        var elementBody = new List<LuauStatement>();
        var restore = EnterElement(cursor, bitBase, arrayField.Element.HeaderBits, loop);
        var element = EmitRead(arrayField.Element, cursor, elementBody);
        restore();
        cursor.Flush(elementBody);

        elementBody.Add(new ExpressionStatement(new BinaryOperator(new ElementAccess(new Identifier(leaf), new Identifier(loop)), "=", element)));

        statements.Add(new NumericForStatement(loop, _one, count, null, new Chunk(elementBody)));
        return new Identifier(leaf);
    }

    /// <summary>
    ///     Guards a conditionally-present payload. The up-front minimum only covers what every payload
    ///     carries, so a branch the sender chose to take has to prove the bytes are actually there -
    ///     otherwise a truncated buffer throws out of the read instead of reporting.
    /// </summary>
    private void EmitBoundsGuard(List<LuauStatement> statements, int byteCount, string path, Cursor cursor)
    {
        if (byteCount <= 0)
            return;

        statements.Add(
            new IfStatement(
                new BinaryOperator(
                    BufferLength(),
                    "<",
                    Add(cursor.Position, new NumberLiteral(byteCount))
                ),
                new Chunk([new Return(BuildErrorTable("truncated", path, null))]),
                [],
                null
            )
        );
    }

    /// <summary>
    ///     Reads a variant tag and rebuilds the selected shape. A tag outside the declared variants
    ///     reports rather than producing a value the union never admitted.
    /// </summary>
    private Identifier EmitUnionRead(UnionField unionField, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = ReserveLocal(LeafName(unionField.Path));
        var tagLocal = ReserveLocal(leaf + "_tag");
        statements.Add(new ConstVariable(tagLocal, null, ReadBits(cursor, unionField.TagBits)));
        statements.Add(new LocalVariable(leaf, null, new NilLiteral()));
        cursor.GoDynamic(statements);
        cursor.Flush(statements);

        var startBit = cursor.BitOffset;
        var widestBit = startBit;
        var chain = new IfChain(statements, new Chunk([new Return(BuildErrorTable("invalid_tag", unionField.Path, null))]));
        for (var index = 0; index < unionField.Variants.Count; index++)
        {
            cursor.BitOffset = startBit;
            var variant = unionField.Variants[index];
            var variantBody = new List<LuauStatement>();
            EmitBoundsGuard(variantBody, VariantBytes(variant), unionField.Path, cursor);

            var rebuilt = RebuildVariant(unionField, variant, cursor, variantBody);
            variantBody.Add(new ExpressionStatement(new BinaryOperator(new Identifier(leaf), "=", rebuilt)));
            widestBit = Math.Max(widestBit, cursor.BitOffset);
            cursor.Flush(variantBody);

            chain.Add(new BinaryOperator(new Identifier(tagLocal), "==", new NumberLiteral(index)), new Chunk(variantBody));
        }

        cursor.BitOffset = widestBit;
        return new Identifier(leaf);
    }

    /// <summary>
    ///     Reconstructs one variant. A literal union carries its whole value in the tag; a discriminated
    ///     one rebuilds the table, restoring the discriminant the tag already encoded.
    /// </summary>
    private LuauExpression RebuildVariant(UnionField unionField, SerializationVariant variant, Cursor cursor, List<LuauStatement> body)
    {
        switch (unionField.Discrimination)
        {
            case UnionDiscrimination.LiteralValue:
                return LuauFactory.Literal(variant.Discriminant);
            case UnionDiscrimination.RuntimeKind:
                return variant.Fields.Count == 1 ? EmitRead(variant.Fields[0], cursor, body) : new NilLiteral();
            
            case UnionDiscrimination.Discriminant:
            default:
                break;
        }

        var initializers = new List<TableInitializer> { new PropertyTableInitializer(unionField.DiscriminantName!, LuauFactory.Literal(variant.Discriminant)) };
        initializers.AddRange(
            variant.Fields.Select(variantField => new PropertyTableInitializer(LeafName(variantField.Path), EmitRead(variantField, cursor, body)))
        );

        return new Table(initializers);
    }

    /// <summary>
    ///     Reads a sentinel tag, rebuilding the well-known value it names or falling through to the
    ///     components. Reserved tags report rather than producing a value the type never allowed.
    /// </summary>
    private Identifier EmitSentinelRead(SerializationField serializationField, Cursor cursor, List<LuauStatement> statements)
    {
        var sentinels = SentinelNamesOf(serializationField)!;
        var leaf = ReserveLocal(LeafName(serializationField.Path));
        var indexLocal = ReserveLocal(leaf + "_sentinel");

        statements.Add(new ConstVariable(indexLocal, null, ReadBits(cursor, BitWidth.ForStateCount(sentinels.Count + 1))));
        statements.Add(new LocalVariable(leaf, null, new NilLiteral()));
        cursor.GoDynamic(statements);
        cursor.Flush(statements);

        var componentBody = new List<LuauStatement>();
        EmitBoundsGuard(componentBody, SentinelComponentBytes(serializationField), serializationField.Path, cursor);

        var rebuilt = serializationField is DatatypeField datatypeField
            ? new Call(
                new Identifier(datatypeField.Datatype.Constructor),
                [.. datatypeField.Datatype.Components.Select(component => ReadNumber(cursor, datatypeField.NumberType, componentBody, ComponentName(datatypeField.Path, component)))]
            )
            : EmitCFrameRead((CFrameField)serializationField, cursor, componentBody);

        componentBody.Add(new ExpressionStatement(new BinaryOperator(new Identifier(leaf), "=", rebuilt)));
        cursor.Flush(componentBody);

        var branches = new List<ElseIfBranch>();
        for (var index = 0; index < sentinels.Count; index++)
            branches.Add(
                new ElseIfBranch(
                    new BinaryOperator(new Identifier(indexLocal), "==", new NumberLiteral(index + 1)),
                    new Chunk([new ExpressionStatement(new BinaryOperator(new Identifier(leaf), "=", new Identifier(sentinels[index])))])
                )
            );

        statements.Add(
            new IfStatement(
                new BinaryOperator(new Identifier(indexLocal), "==", _zero),
                new Chunk(componentBody),
                branches,
                new Chunk([new Return(BuildErrorTable("invalid_tag", serializationField.Path, null))])
            )
        );

        return new Identifier(leaf);
    }

    /// <summary>
    ///     Reads a presence bit, then the payload only when it is set. The local starts nil so an absent
    ///     value needs no else branch.
    /// </summary>
    private Identifier EmitOptionalRead(OptionalField optionalField, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = ReserveLocal(LeafName(optionalField.Path));
        var presentLocal = ReserveLocal(leaf + "_present");
        statements.Add(new ConstVariable(presentLocal, null, new BinaryOperator(ReadBits(cursor, 1), "==", _one)));
        statements.Add(new LocalVariable(leaf, null, new NilLiteral()));
        cursor.GoDynamic(statements);
        cursor.Flush(statements);

        var present = new List<LuauStatement>();
        EmitBoundsGuard(present, optionalField.Inner.BodyBytes ?? 0, optionalField.Path, cursor);

        var inner = EmitRead(optionalField.Inner, cursor, present);
        present.Add(new ExpressionStatement(new BinaryOperator(new Identifier(leaf), "=", inner)));
        cursor.Flush(present);

        statements.Add(new IfStatement(new Identifier(presentLocal), new Chunk(present), [], null));
        return new Identifier(leaf);
    }

    /// <summary>
    ///     Reads a length-prefixed string, checking the prefix against what the buffer actually holds so a
    ///     hostile length reports rather than throwing out of the read.
    /// </summary>
    private Identifier EmitStringRead(StringField stringField, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = ReserveLocal(LeafName(stringField.Path));
        var length = BindRead(ReadNumber(cursor, stringField.LengthType, statements, leaf + "_length"), leaf + "_length", statements);
        cursor.GoDynamic(statements);

        statements.Add(
            new IfStatement(
                new BinaryOperator(BufferLength(), "<", Add(cursor.Position, length)),
                new Chunk([new Return(BuildErrorTable("invalid_length", stringField.Path, null))]),
                [],
                null
            )
        );

        var read = BufferCall("readstring", [new Identifier(BufferLocal), cursor.Position, length]);
        statements.Add(new ConstVariable(leaf, null, read));
        cursor.AdvanceBy(statements, length);

        return new Identifier(leaf);
    }

    private Call EmitCFrameRead(CFrameField cframeField, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = LeafName(cframeField.Path);
        var arguments = _cFramePositionComponents.ConvertAll(component => ReadNumber(
                cursor,
                cframeField.NumberType,
                statements,
                ComponentName(cframeField.Path, component)
            )
        );

        if (cframeField.Encoding == CFrameEncoding.Compressed)
        {
            var packed = cframeField.UseSentinels
                ? ReadNumber(cursor, NumberType.U32, statements, leaf + "_rotation")
                : ReadBits(cursor, CFrameEncodingExtensions.CompressedRotationBits);

            arguments.Add(LuauFactory.RuntimeLibraryCall(["unpack_quaternion"], [packed]));
            return new Call(new Identifier("CFrame.new"), arguments);
        }

        foreach (var component in _quaternionLocals)
            arguments.Add(ReadNumber(cursor, cframeField.NumberType, statements, ComponentName(cframeField.Path, component)));

        return new Call(new Identifier("CFrame.new"), arguments);
    }

    /// <summary>
    ///     Reads one number, naming it after <paramref name="preferred" /> when the value has to outlive the
    ///     read. A dynamic cursor advances between the read and whatever consumes it, so the call cannot stay
    ///     inline; a constant one leaves the expression for the caller to place.
    /// </summary>
    private LuauExpression ReadNumber(Cursor cursor, NumberType numberType, List<LuauStatement> statements, string preferred)
    {
        var call = BufferCall("read" + numberType.BufferSuffix(), [new Identifier(BufferLocal), cursor.Position]);
        if (!cursor.IsDynamic)
        {
            cursor.Advance(numberType.ByteCount());
            return call;
        }

        var local = ReserveLocal(preferred);
        statements.Add(new ConstVariable(local, null, call));
        cursor.Advance(numberType.ByteCount());

        return new Identifier(local);
    }

    /// <summary>
    ///     Names a read that the surrounding code refers to more than once. A dynamic cursor has already
    ///     bound it under the same name, so this only binds the constant-cursor case rather than aliasing
    ///     one local to another.
    /// </summary>
    private Identifier BindRead(LuauExpression value, string preferred, List<LuauStatement> statements)
    {
        if (value is Identifier identifier)
            return identifier;

        var local = ReserveLocal(preferred);
        statements.Add(new ConstVariable(local, null, value));

        return new Identifier(local);
    }

    private readonly HashSet<string> _locals = [];

    /// <summary>
    ///     Reserves names for the body of a loop, releasing them once it is emitted. Luau scopes a loop
    ///     body, so a later sibling loop may reuse the same names - without this the measure pass's
    ///     <c>entries_key</c> pushes the write pass onto a suffixed one nobody can read.
    /// </summary>
    private LocalScope LoopScope() => new(_locals);

    private sealed class LocalScope(HashSet<string> locals) : IDisposable
    {
        private readonly HashSet<string> _outer = [..locals];

        public void Dispose()
        {
            locals.Clear();
            locals.UnionWith(_outer);
        }
    }

    /// <summary>
    ///     Claims a local name, suffixing until it is free. Several constructs name locals after the same
    ///     path leaf - a union's accumulator and the string read filling it, for instance - and the inner
    ///     binding would otherwise shadow the outer, leaving the assignment writing to itself.
    /// </summary>
    private string ReserveLocal(string preferred)
    {
        if (_locals.Add(preferred))
            return preferred;

        for (var suffix = 2;; suffix++)
            if (_locals.Add($"{preferred}_{suffix}"))
                return $"{preferred}_{suffix}";
    }

    private Call ReadBits(Cursor cursor, int bitCount)
    {
        var call = BufferCall("readbits", [new Identifier(BufferLocal), cursor.BitPosition, new NumberLiteral(bitCount)]);
        cursor.BitOffset += bitCount;
        return call;
    }
}