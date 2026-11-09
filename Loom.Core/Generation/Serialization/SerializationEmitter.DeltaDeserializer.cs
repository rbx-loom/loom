using Loom.Core.TypeChecking.Serialization;
using Loom.Luau;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Serialization;

/// <summary>
///     Reading a delta: mirrors the serialization process field for field, reusing
///     the ordinary <see cref="EmitRead" /> wherever a branch resends something in full, and falling back to
///     the baseline's own value - or a further recursive read - wherever it did not.
/// </summary>
internal sealed partial class SerializationEmitter
{
    private enum MapRunKind : byte { Removed, Added, Changed }

    /// <summary>
    ///     The real recursive read. Unlike the ordinary deserializer this does not hand-place a bounds
    ///     guard before every read it introduces - almost everything here is conditional on a bit read
    ///     moments earlier, so nearly every read would need one. Instead <see cref="EmitDeltaDeserializer" />
    ///     calls this under <c>pcall</c>, catching an out-of-bounds buffer access the same way a malformed
    ///     length prefix already is inside the reused <see cref="EmitRead" /> paths (strings, arrays, maps,
    ///     unions), which still return their own precise <c>Result::err</c> rather than throwing.
    /// </summary>
    public Function EmitDeltaReadHelper()
    {
        _locals.Clear();
        _readsBufferLength = false;
        var diffFields = DiffableFields();
        var controlBytes = BitWidth.ToByteCount(diffFields.Sum(f => f.DiffHeaderBits));

        var reads = new List<LuauStatement>();
        var cursor = new Cursor(controlBytes);
        cursor.GoDynamic(reads);

        var initializers = new List<TableInitializer>();
        foreach (var field in schema.Fields)
        {
            if (field is ConstantField constant)
            {
                initializers.Add(new PropertyTableInitializer(LeafName(field.Path), LuauFactory.Literal(constant.Value)));
                continue;
            }

            var baselineValue = Access(new Identifier(BaselineParameter), field.Path);
            initializers.Add(new PropertyTableInitializer(LeafName(field.Path), EmitFieldDiffRead(field, baselineValue, cursor, reads)));
        }

        var body = new List<LuauStatement> { new ConstVariable(BufferLocal, null, new PropertyAccess(new Identifier(DiffParameter), ["buffer"])) };
        body.AddRange(EmitTruncationGuard(controlBytes));

        if (schema.HasBlobs)
        {
            body.Add(new ConstVariable(BlobsLocal, null, new PropertyAccess(new Identifier(DiffParameter), ["blobs"])));
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
            DiffReadHelperName(schema.Interface.Name),
            null,
            [
                new Parameter(BaselineParameter, new TypeName(schema.Interface.Name)),
                new Parameter(DiffParameter, LuauFactory.QualifyRuntimeType(new TypeName("Serialized")))
            ],
            DeserializeResultType(),
            new Chunk(body)
        );
    }

    /// <summary>The exported entry point: runs the real read under <c>pcall</c> and reports a truncated payload rather than crashing on one.</summary>
    public Function EmitDeltaDeserializer()
    {
        var diffFields = DiffableFields();
        var parameters = new List<Parameter>
        {
            new(BaselineParameter, new TypeName(schema.Interface.Name)), new(DiffParameter, LuauFactory.QualifyRuntimeType(new TypeName("Serialized")))
        };

        if (diffFields.Count == 0)
        {
            var initializers = schema.Fields.OfType<ConstantField>()
                .Select(TableInitializer (constant) => new PropertyTableInitializer(LeafName(constant.Path), LuauFactory.Literal(constant.Value)))
                .ToList();

            return new Function(
                ApplyDiffName(schema.Interface.Name),
                null,
                parameters,
                DeserializeResultType(),
                new Chunk(
                    [
                        new Return(
                            new Table([new PropertyTableInitializer("ok", new BooleanLiteral(true)), new PropertyTableInitializer("value", new Table(initializers))])
                        )
                    ]
                )
            );
        }

        const string okLocal = "ok";
        const string resultLocal = "result";
        var body = new List<LuauStatement>
        {
            new MultiConstVariable(
                [okLocal, resultLocal],
                new Call(
                    new Identifier("pcall"),
                    [new Identifier(DiffReadHelperName(schema.Interface.Name)), new Identifier(BaselineParameter), new Identifier(DiffParameter)]
                )
            ),
            new IfStatement(new Identifier(okLocal), new Chunk([new Return(new Identifier(resultLocal))]), [], null),
            new Return(BuildErrorTable("truncated", null, null))
        };

        return new Function(ApplyDiffName(schema.Interface.Name), null, parameters, DeserializeResultType(), new Chunk(body));
    }

    private QualifiedTypeName DeserializeResultType() =>
        LuauFactory.QualifyRuntimeType(
            new TypeName("Result", [new TypeName(schema.Interface.Name), LuauFactory.QualifyRuntimeType(new TypeName("DeserializeError"))])
        );

    /// <summary>Reads one field's diff, mirroring <see cref="EmitFieldDiffWrite" /> case for case.</summary>
    private LuauExpression EmitFieldDiffRead(SerializationField field, LuauExpression baselineValue, Cursor cursor, List<LuauStatement> statements) =>
        field switch
        {
            ConstantField constant => LuauFactory.Literal(constant.Value),
            TupleField tuple => EmitTupleDiffRead(tuple, baselineValue, cursor, statements),
            OptionalField optional => EmitOptionalDiffRead(optional, baselineValue, cursor, statements),
            UnionField union => EmitUnionDiffRead(union, baselineValue, cursor, statements),
            ArrayField array => EmitArrayDiffRead(array, baselineValue, cursor, statements),
            MapField map => EmitMapDiffRead(map, baselineValue, cursor, statements),
            _ => EmitLeafDiffRead(field, baselineValue, cursor, statements)
        };

    private Table EmitTupleDiffRead(TupleField tuple, LuauExpression baselineValue, Cursor cursor, List<LuauStatement> statements)
    {
        var initializers = (
                from element in tuple.Elements
                let elementBaseline = AccessRelative(baselineValue, element.Path, tuple.Path)
                select new PropertyTableInitializer(LeafName(element.Path), EmitFieldDiffRead(element, elementBaseline, cursor, statements))
            )
            .Cast<TableInitializer>()
            .ToList();

        return new Table(initializers);
    }

    private Identifier EmitLeafDiffRead(SerializationField field, LuauExpression baselineValue, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = LeafName(field.Path);
        var changed = ReserveLocal(leaf + "_changed");
        statements.Add(new ConstVariable(changed, null, new BinaryOperator(ReadBits(cursor, 1), "==", _one)));
        var result = ReserveLocal(leaf);
        statements.Add(new LocalVariable(result, null, new NilLiteral()));

        cursor.Flush(statements);

        var resend = new List<LuauStatement>();
        var resendValue = EmitRead(field, cursor, resend);
        resend.Add(new ExpressionStatement(new BinaryOperator(new Identifier(result), "=", resendValue)));
        cursor.Flush(resend);

        var unchanged = new List<LuauStatement> { new ExpressionStatement(new BinaryOperator(new Identifier(result), "=", baselineValue)) };

        statements.Add(new IfStatement(new Identifier(changed), new Chunk(resend), [], new Chunk(unchanged)));
        return new Identifier(result);
    }

    private Identifier EmitOptionalDiffRead(OptionalField optional, LuauExpression baselineValue, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = LeafName(optional.Path);
        var changed = ReserveLocal(leaf + "_presence_changed");
        statements.Add(new ConstVariable(changed, null, new BinaryOperator(ReadBits(cursor, 1), "==", _one)));
        var result = ReserveLocal(leaf);
        statements.Add(new LocalVariable(result, null, new NilLiteral()));

        cursor.Flush(statements);
        var startBit = cursor.BitOffset;
        var widestBit = startBit;

        var resend = new List<LuauStatement>();
        var resendValue = EmitRead(optional, cursor, resend);
        resend.Add(new ExpressionStatement(new BinaryOperator(new Identifier(result), "=", resendValue)));
        widestBit = Math.Max(widestBit, cursor.BitOffset);
        cursor.Flush(resend);

        cursor.BitOffset = startBit;
        var innerStatements = new List<LuauStatement>();
        var innerBaseline = AccessRelative(baselineValue, optional.Inner.Path, optional.Path);
        var innerRead = EmitFieldDiffRead(optional.Inner, innerBaseline, cursor, innerStatements);
        innerStatements.Add(new ExpressionStatement(new BinaryOperator(new Identifier(result), "=", innerRead)));
        widestBit = Math.Max(widestBit, cursor.BitOffset);
        cursor.Flush(innerStatements);

        var unchanged = new List<LuauStatement>();
        if (innerStatements.Count > 0)
            unchanged.Add(new IfStatement(IsPresent(baselineValue), new Chunk(innerStatements), [], null));

        statements.Add(new IfStatement(new Identifier(changed), new Chunk(resend), [], new Chunk(unchanged)));
        cursor.BitOffset = widestBit;
        return new Identifier(result);
    }

    private Identifier EmitUnionDiffRead(UnionField union, LuauExpression baselineValue, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = LeafName(union.Path);
        var changed = ReserveLocal(leaf + "_tag_changed");
        statements.Add(new ConstVariable(changed, null, new BinaryOperator(ReadBits(cursor, 1), "==", _one)));
        var result = ReserveLocal(leaf);
        statements.Add(new LocalVariable(result, null, new NilLiteral()));

        cursor.Flush(statements);
        var startBit = cursor.BitOffset;
        var widestBit = startBit;

        var resend = new List<LuauStatement>();
        var resendValue = EmitRead(union, cursor, resend);
        resend.Add(new ExpressionStatement(new BinaryOperator(new Identifier(result), "=", resendValue)));
        widestBit = Math.Max(widestBit, cursor.BitOffset);
        cursor.Flush(resend);

        cursor.BitOffset = startBit;
        var unchanged = new List<LuauStatement>();
        var baselineTag = ResolveUnionTag(union, baselineValue, leaf + "_tag_b", unchanged);
        var chain = new IfChain(unchanged);
        for (var index = 0; index < union.Variants.Count; index++)
        {
            cursor.BitOffset = startBit;
            var variant = union.Variants[index];
            var variantStatements = new List<LuauStatement>();
            var rebuilt = RebuildVariantDiff(union, variant, baselineValue, cursor, variantStatements);
            variantStatements.Add(new ExpressionStatement(new BinaryOperator(new Identifier(result), "=", rebuilt)));
            widestBit = Math.Max(widestBit, cursor.BitOffset);
            cursor.Flush(variantStatements);

            chain.Add(new BinaryOperator(baselineTag, "==", new NumberLiteral(index)), new Chunk(variantStatements));
        }

        statements.Add(new IfStatement(new Identifier(changed), new Chunk(resend), [], new Chunk(unchanged)));
        cursor.BitOffset = widestBit;
        return new Identifier(result);
    }

    /// <summary>Rebuilds one variant against baseline, recursing field by field instead of reading each in full.</summary>
    private LuauExpression RebuildVariantDiff(
        UnionField union,
        SerializationVariant variant,
        LuauExpression baselineValue,
        Cursor cursor,
        List<LuauStatement> statements)
    {
        switch (union.Discrimination)
        {
            case UnionDiscrimination.LiteralValue:
                return LuauFactory.Literal(variant.Discriminant);
            case UnionDiscrimination.RuntimeKind:
            {
                if (variant.Fields is not [var onlyField])
                    return new NilLiteral();

                var fieldBaseline = AccessRelative(baselineValue, onlyField.Path, union.Path);
                return EmitFieldDiffRead(onlyField, fieldBaseline, cursor, statements);
            }
            case UnionDiscrimination.Discriminant:
            default:
            {
                var initializers = new List<TableInitializer> { new PropertyTableInitializer(union.DiscriminantName!, LuauFactory.Literal(variant.Discriminant)) };
                initializers.AddRange(
                    from variantField in variant.Fields
                    let fieldBaseline = AccessRelative(baselineValue, variantField.Path, union.Path)
                    select new PropertyTableInitializer(LeafName(variantField.Path), EmitFieldDiffRead(variantField, fieldBaseline, cursor, statements))
                );

                return new Table(initializers);
            }
        }
    }

    private Identifier EmitArrayDiffRead(ArrayField array, LuauExpression baselineValue, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = LeafName(array.Path);
        var changed = ReserveLocal(leaf + "_length_changed");
        statements.Add(new ConstVariable(changed, null, new BinaryOperator(ReadBits(cursor, 1), "==", _one)));
        var result = ReserveLocal(leaf);
        statements.Add(new LocalVariable(result, null, new NilLiteral()));

        cursor.Flush(statements);

        var resend = new List<LuauStatement>();
        var resendValue = EmitRead(array, cursor, resend);
        resend.Add(new ExpressionStatement(new BinaryOperator(new Identifier(result), "=", resendValue)));
        cursor.Flush(resend);

        var unchanged = new List<LuauStatement>();
        var countLocal = ReserveLocal(leaf + "_count");
        unchanged.Add(new ConstVariable(countLocal, null, Length(baselineValue)));
        var bitBase = ReserveElementBits(array.Element.DiffHeaderBits, leaf, new Identifier(countLocal), cursor, unchanged);
        unchanged.Add(new ExpressionStatement(new BinaryOperator(new Identifier(result), "=", Table.Empty)));
        cursor.Flush(unchanged);

        var loop = ReserveLocal(LoopLocal);
        var elementBaseline = new ElementAccess(baselineValue, new Identifier(loop));
        var elementStatements = new List<LuauStatement>();
        var restore = EnterElement(cursor, bitBase, array.Element.DiffHeaderBits, loop);
        var elementRead = EmitFieldDiffRead(array.Element, elementBaseline, cursor, elementStatements);
        restore();
        cursor.Flush(elementStatements);

        elementStatements.Add(new ExpressionStatement(new BinaryOperator(new ElementAccess(new Identifier(result), new Identifier(loop)), "=", elementRead)));

        unchanged.Add(new NumericForStatement(loop, _one, new Identifier(countLocal), null, new Chunk(elementStatements)));

        statements.Add(new IfStatement(new Identifier(changed), new Chunk(resend), [], new Chunk(unchanged)));
        return new Identifier(result);
    }

    private Identifier EmitMapDiffRead(MapField map, LuauExpression baselineValue, Cursor cursor, List<LuauStatement> statements)
    {
        var leaf = LeafName(map.Path);
        var result = ReserveLocal(leaf);
        statements.Add(new ConstVariable(result, null, Table.Empty));

        var copyKey = ReserveLocal(leaf + "_copy_key");
        var copyValue = ReserveLocal(leaf + "_copy_value");
        statements.Add(
            new ForStatement(
                [copyKey, copyValue],
                baselineValue,
                new Chunk(
                    [new ExpressionStatement(new BinaryOperator(new ElementAccess(new Identifier(result), new Identifier(copyKey)), "=", new Identifier(copyValue)))]
                )
            )
        );

        ReadMapKeyRun(
            map,
            result,
            baselineValue,
            cursor,
            statements,
            MapRunKind.Removed
        );

        ReadMapKeyRun(
            map,
            result,
            baselineValue,
            cursor,
            statements,
            MapRunKind.Added
        );

        ReadMapKeyRun(
            map,
            result,
            baselineValue,
            cursor,
            statements,
            MapRunKind.Changed
        );

        return new Identifier(result);
    }

    /// <summary>One of a map diff's three count-prefixed sections, applying it onto the shallow-copied result.</summary>
    private void ReadMapKeyRun(MapField map, string resultLocal, LuauExpression baselineValue, Cursor cursor, List<LuauStatement> statements, MapRunKind kind)
    {
        var leaf = LeafName(map.Path);
        var countName = $"{leaf}_{kind}_count".ToLowerInvariant();
        var count = BindRead(ReadNumber(cursor, map.LengthType, statements, countName), countName, statements);
        var entryBits = kind == MapRunKind.Changed
            ? ReserveElementBits(map.DiffEntryBits, leaf + "_entry", count, cursor, statements)
            : null;

        cursor.Flush(statements);

        var loop = ReserveLocal(LoopLocal);
        var loopBody = new List<LuauStatement>();
        var key = EmitRead(map.Key, cursor, loopBody);
        var keyLocal = ReserveLocal($"{leaf}_{kind}_key".ToLowerInvariant());
        loopBody.Add(new ConstVariable(keyLocal, null, key));

        var target = new ElementAccess(new Identifier(resultLocal), new Identifier(keyLocal));
        switch (kind)
        {
            case MapRunKind.Removed:
                loopBody.Add(new ExpressionStatement(new BinaryOperator(target, "=", new NilLiteral())));
                break;

            case MapRunKind.Added:
            {
                var value = EmitRead(map.Value, cursor, loopBody);
                loopBody.Add(new ExpressionStatement(new BinaryOperator(target, "=", value)));
                break;
            }

            case MapRunKind.Changed:
            {
                var restore = EnterElement(cursor, entryBits, map.DiffEntryBits, loop);
                var entryBaseline = new ElementAccess(baselineValue, new Identifier(keyLocal));
                var value = EmitFieldDiffRead(map.Value, entryBaseline, cursor, loopBody);
                restore();
                loopBody.Add(new ExpressionStatement(new BinaryOperator(target, "=", value)));
                break;
            }
        }

        cursor.Flush(loopBody);
        statements.Add(new NumericForStatement(loop, _one, count, null, new Chunk(loopBody)));
    }
}