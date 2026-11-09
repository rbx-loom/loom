using Loom.Core.TypeChecking.Serialization;
using Loom.Luau;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Serialization;

/// <summary>
///     Writing a delta: only what differs between a baseline and a current value goes on the wire, diffed
///     recursively into structs, unions, optionals, array elements and map entries rather than resending a
///     field wholesale. Reuses the ordinary per-field writer (<see cref="EmitValueWrite" />) wherever a
///     branch decides to resend something in full, so this file is purely the "did it change, and if not,
///     dig deeper" orchestration on top of machinery the serializer already
///     has to get right.
/// </summary>
internal sealed partial class SerializationEmitter
{
    /// <summary>
    ///     The real recursive write, parameterised over the destination buffer rather than allocating one
    ///     itself. Exact upfront sizing here would need a second recursive pass mirroring every branch
    ///     below - precisely the kind of code the original serializer's size arithmetic shipped the most
    ///     bugs in - so instead this can simply overrun a buffer that turned out too small, and the caller
    ///     (<see cref="EmitDeltaAttempt" />) retries with a bigger one. Returns the number of bytes actually
    ///     used, and the blobs array when the schema can produce one.
    /// </summary>
    public Function EmitDeltaWriteHelper()
    {
        _locals.Clear();
        _boundCounts.Clear();
        var diffFields = DiffableFields();
        var body = new List<LuauStatement>();

        if (schema.HasBlobs)
            body.Add(new ConstVariable(BlobsLocal, null, Table.Empty));

        var controlBits = diffFields.Sum(f => f.DiffHeaderBits);
        var cursor = new Cursor(BitWidth.ToByteCount(controlBits));
        cursor.GoDynamic(body);

        foreach (var field in diffFields)
            EmitFieldDiffWrite(field, Access(new Identifier(BaselineParameter), field.Path), Access(new Identifier(ValueParameter), field.Path), cursor, body);

        cursor.Flush(body);
        var returns = new List<LuauExpression> { cursor.Position };
        if (schema.HasBlobs)
            returns.Add(new Identifier(BlobsLocal));

        body.Add(new MultiReturn(returns));

        return new Function(
            DiffWriteHelperName(schema.Interface.Name),
            null,
            [
                new Parameter(BaselineParameter, new TypeName(schema.Interface.Name)),
                new Parameter(ValueParameter, new TypeName(schema.Interface.Name)),
                new Parameter(BufferLocal, new TypeName("buffer"))
            ],
            null,
            new Chunk(body)
        );
    }

    /// <summary>
    ///     Allocates a scratch buffer of <c>size</c> bytes and hands it to the write helper
    ///     under <c>pcall</c>; a buffer that turned out too small throws, which is caught here and retried
    ///     at double the size. Converges in at most a couple of attempts even when the initial estimate is
    ///     badly off, and never in the common case where it wasn't.
    /// </summary>
    public Function EmitDeltaAttempt()
    {
        const string sizeParameter = "size";
        const string scratchLocal = "scratch";
        const string okLocal = "ok";
        const string writtenLocal = "written";

        var writeArguments = new List<LuauExpression> { new Identifier(BaselineParameter), new Identifier(ValueParameter), new Identifier(scratchLocal) };
        var pcallResultNames = new List<string> { okLocal, writtenLocal };
        if (schema.HasBlobs)
            pcallResultNames.Add(BlobsLocal);

        var body = new List<LuauStatement>
        {
            new ConstVariable(scratchLocal, null, BufferCall("create", [new Identifier(sizeParameter)])),
            new MultiConstVariable(
                pcallResultNames,
                new Call(new Identifier("pcall"), [new Identifier(DiffWriteHelperName(schema.Interface.Name)), .. writeArguments])
            )
        };

        var successReturn = new List<LuauExpression> { new Identifier(scratchLocal), new Identifier(writtenLocal) };
        if (schema.HasBlobs)
            successReturn.Add(new Identifier(BlobsLocal));

        body.Add(
            new IfStatement(
                new Identifier(okLocal),
                new Chunk([new MultiReturn(successReturn)]),
                [],
                null
            )
        );

        body.Add(
            new MultiReturn(
                [
                    new Call(
                        new Identifier(DiffAttemptHelperName(schema.Interface.Name)),
                        [new Identifier(BaselineParameter), new Identifier(ValueParameter), Multiply(new Identifier(sizeParameter), new NumberLiteral(2))]
                    )
                ]
            )
        );

        return new Function(
            DiffAttemptHelperName(schema.Interface.Name),
            null,
            [
                new Parameter(BaselineParameter, new TypeName(schema.Interface.Name)),
                new Parameter(ValueParameter, new TypeName(schema.Interface.Name)),
                new Parameter(sizeParameter, PrimitiveType.Number)
            ],
            null,
            new Chunk(body)
        );
    }

    /// <summary>
    ///     The exported entry point. Estimates a starting size from the two full serializations' combined
    ///     length - never smaller than any diff this file can produce, since no branch below ever writes
    ///     more than "baseline's removal cost plus current's full cost" - plus the exact, compile-time-known
    ///     control-bit budget, then trims the scratch buffer <see cref="EmitDeltaAttempt" /> settled on down
    ///     to the bytes actually used.
    /// </summary>
    public Function EmitDeltaSerializer()
    {
        var diffFields = DiffableFields();
        if (diffFields.Count == 0)
            return new Function(
                DiffName(schema.Interface.Name),
                null,
                [new Parameter(BaselineParameter, new TypeName(schema.Interface.Name)), new Parameter(ValueParameter, new TypeName(schema.Interface.Name))],
                LuauFactory.QualifyRuntimeType(new TypeName("Serialized")),
                new Chunk([new Return(Table.Empty)])
            );

        const string baselineSizeLocal = "baseline_s";
        const string valueSizeLocal = "value_s";
        const string sizeLocal = "size";
        const string scratchLocal = "scratch";
        const string writtenLocal = "written";
        const string resultLocal = "result";

        var controlBits = diffFields.Sum(f => f.DiffHeaderBits);
        var controlBytes = BitWidth.ToByteCount(controlBits);

        var body = new List<LuauStatement>
        {
            new ConstVariable(baselineSizeLocal, null, new Call(new Identifier(SerializeName(schema.Interface.Name)), [new Identifier(BaselineParameter)])),
            new ConstVariable(valueSizeLocal, null, new Call(new Identifier(SerializeName(schema.Interface.Name)), [new Identifier(ValueParameter)])),
            new ConstVariable(
                sizeLocal,
                null,
                Add(Add(BufferLenOf(baselineSizeLocal), BufferLenOf(valueSizeLocal)), new NumberLiteral(controlBytes))
            )
        };

        var attemptResults = new List<string> { scratchLocal, writtenLocal };
        if (schema.HasBlobs)
            attemptResults.Add(BlobsLocal);

        body.Add(
            new MultiConstVariable(
                attemptResults,
                new Call(
                    new Identifier(DiffAttemptHelperName(schema.Interface.Name)),
                    [new Identifier(BaselineParameter), new Identifier(ValueParameter), new Identifier(sizeLocal)]
                )
            )
        );

        body.Add(new ConstVariable(resultLocal, null, BufferCall("create", [new Identifier(writtenLocal)])));
        body.Add(
            new ExpressionStatement(
                BufferCall(
                    "copy",
                    [new Identifier(resultLocal), _zero, new Identifier(scratchLocal), _zero, new Identifier(writtenLocal)]
                )
            )
        );

        var resultInitializers = new List<TableInitializer> { new PropertyTableInitializer("buffer", new Identifier(resultLocal)) };
        if (schema.HasBlobs)
            resultInitializers.Add(new PropertyTableInitializer("blobs", new Identifier(BlobsLocal)));

        body.Add(new Return(new Table(resultInitializers)));

        return new Function(
            DiffName(schema.Interface.Name),
            null,
            [new Parameter(BaselineParameter, new TypeName(schema.Interface.Name)), new Parameter(ValueParameter, new TypeName(schema.Interface.Name))],
            LuauFactory.QualifyRuntimeType(new TypeName("Serialized")),
            new Chunk(body)
        );
    }

    /// <summary>Fields the delta format has anything to say about - a literal-typed field never differs.</summary>
    private List<SerializationField> DiffableFields() => [.. schema.Fields.Where(f => f is not ConstantField)];

    private static IfExpression BufferLenOf(string serializedLocal)
    {
        var buffer = new PropertyAccess(new Identifier(serializedLocal), ["buffer"]);
        return new IfExpression(IsPresent(buffer), BufferLenCall(buffer), [], _zero);
    }

    private static Call BufferLenCall(LuauExpression buffer) => new(new PropertyAccess(new Identifier("buffer"), ["len"]), [buffer]);

    private static Call DeepEqual(LuauExpression baselineValue, LuauExpression currentValue) =>
        LuauFactory.RuntimeLibraryCall(["deep_equal"], [baselineValue, currentValue]);

    /// <summary>
    ///     Resolves a union's tag against an arbitrary value, under a caller-supplied name so a diff - which
    ///     needs the tag on both sides of the comparison - can resolve two without one shadowing the other.
    ///     Mirrors <see cref="EmitUnionTag" />, which cannot be reused directly since it always names its
    ///     locals after the field's path alone.
    /// </summary>
    private Identifier ResolveUnionTag(UnionField unionField, LuauExpression value, string preferredName, List<LuauStatement> body)
    {
        var subject = value;
        if (subject is not Identifier)
        {
            subject = new Identifier(ReserveLocal(preferredName + "_value"));
            body.Add(new ConstVariable(((Identifier)subject).Name, null, value));
        }

        var tagLocal = ReserveLocal(preferredName);
        body.Add(new LocalVariable(tagLocal, null, _zero));

        var chain = new IfChain(body);
        for (var index = 1; index < unionField.Variants.Count; index++)
        {
            var condition = VariantCondition(unionField, subject, index);
            chain.Add(condition, new Chunk([new ExpressionStatement(new BinaryOperator(new Identifier(tagLocal), "=", new NumberLiteral(index)))]));
        }

        return new Identifier(tagLocal);
    }

    /// <summary>
    ///     Diffs one field, appending whatever it takes to <paramref name="body" />. Every case follows the
    ///     same shape: write however many bits (<see cref="SerializationField.DiffHeaderBits" />) the field
    ///     needs to say what happened, then either fully resend through the ordinary <see cref="EmitValueWrite" />
    ///     or recurse into whatever changed. The cursor is dynamic on entry, so every branch is free to write
    ///     a different number of bytes.
    /// </summary>
    private void EmitFieldDiffWrite(SerializationField field, LuauExpression baselineValue, LuauExpression currentValue, Cursor cursor, List<LuauStatement> body)
    {
        switch (field)
        {
            case ConstantField:
                return;

            case TupleField tuple:
                foreach (var element in tuple.Elements)
                {
                    var elementBaseline = AccessRelative(baselineValue, element.Path, tuple.Path);
                    var elementCurrent = AccessRelative(currentValue, element.Path, tuple.Path);
                    EmitFieldDiffWrite(element, elementBaseline, elementCurrent, cursor, body);
                }

                return;

            case OptionalField optional:
                EmitOptionalDiffWrite(optional, baselineValue, currentValue, cursor, body);
                return;

            case UnionField union:
                EmitUnionDiffWrite(union, baselineValue, currentValue, cursor, body);
                return;

            case ArrayField array:
                EmitArrayDiffWrite(array, baselineValue, currentValue, cursor, body);
                return;

            case MapField map:
                EmitMapDiffWrite(map, baselineValue, currentValue, cursor, body);
                return;

            default:
                EmitLeafDiffWrite(field, baselineValue, currentValue, cursor, body);
                return;
        }
    }

    private void EmitLeafDiffWrite(SerializationField field, LuauExpression baselineValue, LuauExpression currentValue, Cursor cursor, List<LuauStatement> body)
    {
        var leaf = LeafName(field.Path);
        var changed = ReserveLocal(leaf + "_changed");
        body.Add(new ConstVariable(changed, null, new UnaryOperator("not ", DeepEqual(baselineValue, currentValue))));
        body.Add(new ExpressionStatement(WriteBits(cursor, 1, new IfExpression(new Identifier(changed), _one, [], _zero))));

        cursor.Flush(body);

        var resend = new List<LuauStatement>();
        ResolveSentinel(field, currentValue, resend);
        EmitValueWrite(field, currentValue, cursor, resend);
        cursor.Flush(resend);
        if (resend.Count > 0)
            body.Add(new IfStatement(new Identifier(changed), new Chunk(resend), [], null));
    }

    /// <summary>
    ///     Binds a sentinelled field's value and resolves which sentinel it matched, the same way
    ///     <see cref="EmitSentinelPrologue" /> does for the whole schema up front - except <see cref="EmitSentinelPrologue" />
    ///     only ever runs once, against <c>value</c>, and this emitter's delta functions never call it, so a
    ///     changed sentinelled leaf has to resolve its own sentinel here before <see cref="EmitValueWrite" />
    ///     reads the locals it expects to already exist. A no-op for anything that isn't sentinelled.
    /// </summary>
    private void ResolveSentinel(SerializationField field, LuauExpression value, List<LuauStatement> body)
    {
        if (SentinelNamesOf(field) is not { Count: > 0 } sentinels)
            return;

        var valueLocal = SentinelValueLocal(field.Path);
        var indexLocal = SentinelIndexLocal(field.Path);
        body.Add(new ConstVariable(valueLocal, null, value));
        body.Add(new LocalVariable(indexLocal, null, _zero));

        var chain = new IfChain(body);
        for (var index = 0; index < sentinels.Count; index++)
        {
            var condition = new BinaryOperator(new Identifier(valueLocal), "==", new Identifier(sentinels[index]));
            chain.Add(condition, new Chunk([new ExpressionStatement(new BinaryOperator(new Identifier(indexLocal), "=", new NumberLiteral(index + 1)))]));
        }
    }

    /// <summary>
    ///     A presence-changed bit, then either a full resend (new presence plus a fresh value) or, when the
    ///     value was and still is present, a recursive dive into it. The two branches restart at the same
    ///     bit offset and the wider one sizes the range, exactly as live union variants already share theirs.
    /// </summary>
    private void EmitOptionalDiffWrite(OptionalField optional, LuauExpression baselineValue, LuauExpression currentValue, Cursor cursor, List<LuauStatement> body)
    {
        var leaf = LeafName(optional.Path);
        var changed = ReserveLocal(leaf + "_presence_changed");
        body.Add(
            new ConstVariable(changed, null, new BinaryOperator(new Parenthesized(IsPresent(baselineValue)), "~=", new Parenthesized(IsPresent(currentValue))))
        );

        body.Add(new ExpressionStatement(WriteBits(cursor, 1, new IfExpression(new Identifier(changed), _one, [], _zero))));

        cursor.Flush(body);
        var startBit = cursor.BitOffset;
        var widestBit = startBit;

        var resend = new List<LuauStatement>();
        EmitValueWrite(optional, currentValue, cursor, resend);
        widestBit = Math.Max(widestBit, cursor.BitOffset);
        cursor.Flush(resend);

        cursor.BitOffset = startBit;
        var innerWrite = new List<LuauStatement>();
        var innerBaseline = AccessRelative(baselineValue, optional.Inner.Path, optional.Path);
        var innerCurrent = AccessRelative(currentValue, optional.Inner.Path, optional.Path);
        EmitFieldDiffWrite(optional.Inner, innerBaseline, innerCurrent, cursor, innerWrite);
        widestBit = Math.Max(widestBit, cursor.BitOffset);
        cursor.Flush(innerWrite);

        var unchanged = new List<LuauStatement>();
        if (innerWrite.Count > 0)
            unchanged.Add(new IfStatement(IsPresent(currentValue), new Chunk(innerWrite), [], null));

        body.Add(new IfStatement(new Identifier(changed), new Chunk(resend), [], new Chunk(unchanged)));
        cursor.BitOffset = widestBit;
    }

    /// <summary>
    ///     A tag-changed bit, then either a full resend of the new variant or, when the active variant is
    ///     unchanged, a recursive dive into each of its fields. Same overlap technique as <see cref="EmitOptionalDiffWrite" />.
    /// </summary>
    private void EmitUnionDiffWrite(UnionField union, LuauExpression baselineValue, LuauExpression currentValue, Cursor cursor, List<LuauStatement> body)
    {
        var leaf = LeafName(union.Path);
        var baselineTag = ResolveUnionTag(union, baselineValue, leaf + "_tag_b", body);
        var currentTag = ResolveUnionTag(union, currentValue, leaf + "_tag_c", body);
        var changed = ReserveLocal(leaf + "_tag_changed");
        body.Add(new ConstVariable(changed, null, new BinaryOperator(baselineTag, "~=", currentTag)));
        body.Add(new ExpressionStatement(WriteBits(cursor, 1, new IfExpression(new Identifier(changed), _one, [], _zero))));

        cursor.Flush(body);
        var startBit = cursor.BitOffset;
        var widestBit = startBit;

        var resend = new List<LuauStatement>();
        _resolvedTags[union.Path] = currentTag.Name;
        EmitValueWrite(union, currentValue, cursor, resend);
        _resolvedTags.Remove(union.Path);
        widestBit = Math.Max(widestBit, cursor.BitOffset);
        cursor.Flush(resend);

        var unchanged = new List<LuauStatement>();
        var chain = new IfChain(unchanged);
        for (var index = 0; index < union.Variants.Count; index++)
        {
            cursor.BitOffset = startBit;
            var variant = union.Variants[index];
            var variantBody = new List<LuauStatement>();
            foreach (var variantField in variant.Fields)
            {
                var fieldBaseline = AccessRelative(baselineValue, variantField.Path, union.Path);
                var fieldCurrent = AccessRelative(currentValue, variantField.Path, union.Path);
                EmitFieldDiffWrite(variantField, fieldBaseline, fieldCurrent, cursor, variantBody);
            }

            widestBit = Math.Max(widestBit, cursor.BitOffset);
            cursor.Flush(variantBody);
            if (variantBody.Count == 0)
                continue;

            chain.Add(new BinaryOperator(currentTag, "==", new NumberLiteral(index)), new Chunk(variantBody));
        }

        body.Add(new IfStatement(new Identifier(changed), new Chunk(resend), [], new Chunk(unchanged)));
        cursor.BitOffset = widestBit;
    }

    /// <summary>
    ///     A length-changed bit, then either a full resend or, for an unchanged length, a per-element diff
    ///     sharing a block sized by the element's own <see cref="SerializationField.DiffHeaderBits" /> - the
    ///     same shared-block technique <see cref="ReserveElementBits" /> already gives an array whose
    ///     elements need header bits at all.
    /// </summary>
    private void EmitArrayDiffWrite(ArrayField array, LuauExpression baselineValue, LuauExpression currentValue, Cursor cursor, List<LuauStatement> body)
    {
        var leaf = LeafName(array.Path);

        var count = BindCount(currentValue, leaf, body);
        var changed = ReserveLocal(leaf + "_length_changed");
        body.Add(new ConstVariable(changed, null, new BinaryOperator(Length(baselineValue), "~=", count)));
        body.Add(new ExpressionStatement(WriteBits(cursor, 1, new IfExpression(new Identifier(changed), _one, [], _zero))));

        cursor.Flush(body);

        var resend = new List<LuauStatement>();
        _boundCounts[array] = count.Name;
        EmitValueWrite(array, currentValue, cursor, resend);
        _boundCounts.Remove(array);
        cursor.Flush(resend);

        var unchanged = new List<LuauStatement>();

        var bitBase = ReserveElementBits(array.Element.DiffHeaderBits, leaf, count, cursor, unchanged);

        using var elementScope = LoopScope();
        var loop = ReserveLocal(LoopLocal);

        var elementLeaf = LeafName(array.Element.Path);
        var elementBaseline = new Identifier(ReserveLocal(elementLeaf + "_baseline"));
        var elementCurrent = new Identifier(ReserveLocal(elementLeaf));
        var elementBody = new List<LuauStatement>
        {
            new ConstVariable(elementBaseline.Name, null, new ElementAccess(baselineValue, new Identifier(loop))),
            new ConstVariable(elementCurrent.Name, null, new ElementAccess(currentValue, new Identifier(loop)))
        };

        cursor.Flush(unchanged);
        var restore = EnterElement(cursor, bitBase, array.Element.DiffHeaderBits, loop);
        EmitFieldDiffWrite(array.Element, elementBaseline, elementCurrent, cursor, elementBody);
        restore();
        cursor.Flush(elementBody);
        unchanged.Add(new NumericForStatement(loop, _one, count, null, new Chunk(elementBody)));

        body.Add(new IfStatement(new Identifier(changed), new Chunk(resend), [], new Chunk(unchanged)));
    }

    /// <summary>
    ///     Maps are unordered and their keys are dynamic, so unlike every other shape there is no fixed set
    ///     of "fields" to gate behind bits - the diff is entirely body-driven. A classification pass splits
    ///     current's keys into added (absent from baseline) and changed (present but different, per
    ///     <c>Loom.deep_equal</c>), and baseline's keys not in current become removed. Three
    ///     count-prefixed sections follow: removed keys alone, added key-value pairs in full, and changed
    ///     keys paired with a recursive dive into the value - the only place a map's entries share a block,
    ///     sized by <see cref="MapField.DiffEntryBits" />.
    /// </summary>
    private void EmitMapDiffWrite(MapField map, LuauExpression baselineValue, LuauExpression currentValue, Cursor cursor, List<LuauStatement> body)
    {
        var leaf = LeafName(map.Path);
        var removed = ReserveLocal(leaf + "_removed");
        var added = ReserveLocal(leaf + "_added");
        var changed = ReserveLocal(leaf + "_changed");
        body.Add(new ConstVariable(removed, null, Table.Empty));
        body.Add(new ConstVariable(added, null, Table.Empty));
        body.Add(new ConstVariable(changed, null, Table.Empty));

        using (LoopScope())
        {
            var currentKey = ReserveLocal(leaf + "_current_key");
            var currentEntry = ReserveLocal(leaf + "_current_entry");
            var baselineEntry = ReserveLocal(leaf + "_baseline_entry");
            var classifyCurrent = new List<LuauStatement>
            {
                new ConstVariable(baselineEntry, null, new ElementAccess(baselineValue, new Identifier(currentKey))),
                new IfStatement(
                    new BinaryOperator(new Identifier(baselineEntry), "==", new NilLiteral()),
                    new Chunk([new ExpressionStatement(LuauFactory.TableCall("insert", [new Identifier(added), new Identifier(currentKey)]))]),
                    [],
                    new Chunk(
                        [
                            new IfStatement(
                                new UnaryOperator("not ", DeepEqual(new Identifier(baselineEntry), new Identifier(currentEntry))),
                                new Chunk([new ExpressionStatement(LuauFactory.TableCall("insert", [new Identifier(changed), new Identifier(currentKey)]))]),
                                [],
                                null
                            )
                        ]
                    )
                )
            };

            body.Add(new ForStatement([currentKey, currentEntry], currentValue, new Chunk(classifyCurrent)));
        }

        using (LoopScope())
        {
            var baselineKey = ReserveLocal(leaf + "_baseline_key");
            var classifyBaseline = new List<LuauStatement>
            {
                new IfStatement(
                    new BinaryOperator(new ElementAccess(currentValue, new Identifier(baselineKey)), "==", new NilLiteral()),
                    new Chunk([new ExpressionStatement(LuauFactory.TableCall("insert", [new Identifier(removed), new Identifier(baselineKey)]))]),
                    [],
                    null
                )
            };

            body.Add(new ForStatement([baselineKey], baselineValue, new Chunk(classifyBaseline)));
        }

        WriteMapKeyRun(
            map,
            removed,
            cursor,
            body,
            includeValue: false,
            recurseValue: false
        );

        WriteMapKeyRun(
            map,
            added,
            cursor,
            body,
            includeValue: true,
            recurseValue: false,
            currentValue: currentValue
        );

        WriteMapKeyRun(
            map,
            changed,
            cursor,
            body,
            includeValue: true,
            recurseValue: true,
            baselineValue: baselineValue,
            currentValue: currentValue
        );
    }

    /// <summary>One of a map diff's three count-prefixed sections: a run of keys, optionally paired with a value.</summary>
    private void WriteMapKeyRun(
        MapField map,
        string keysLocal,
        Cursor cursor,
        List<LuauStatement> body,
        bool includeValue,
        bool recurseValue,
        LuauExpression? baselineValue = null,
        LuauExpression? currentValue = null)
    {
        var leaf = LeafName(map.Path);
        var count = BindCount(new Identifier(keysLocal), keysLocal, body);
        WriteNumber(cursor, map.LengthType, count, body);

        var bitBase = includeValue && recurseValue
            ? ReserveElementBits(map.DiffEntryBits, leaf + "_entry", count, cursor, body)
            : null;

        cursor.Flush(body);

        using var entryScope = LoopScope();
        var loop = ReserveLocal(LoopLocal);
        var boundKey = ReserveLocal(leaf + "_key");
        var loopBody = new List<LuauStatement> { new ConstVariable(boundKey, null, new ElementAccess(new Identifier(keysLocal), new Identifier(loop))) };
        EmitValueWrite(map.Key, new Identifier(boundKey), cursor, loopBody);

        if (includeValue && !recurseValue)
        {
            var addedEntry = new Identifier(ReserveLocal(leaf + "_entry"));
            loopBody.Add(new ConstVariable(addedEntry.Name, null, new ElementAccess(currentValue!, new Identifier(boundKey))));
            EmitValueWrite(map.Value, addedEntry, cursor, loopBody);
            cursor.Flush(loopBody);
            body.Add(new NumericForStatement(loop, _one, count, null, new Chunk(loopBody)));
            return;
        }

        if (includeValue)
        {
            var baselineEntry = new Identifier(ReserveLocal(leaf + "_baseline_entry"));
            var currentEntry = new Identifier(ReserveLocal(leaf + "_entry"));
            loopBody.Add(new ConstVariable(baselineEntry.Name, null, new ElementAccess(baselineValue!, new Identifier(boundKey))));
            loopBody.Add(new ConstVariable(currentEntry.Name, null, new ElementAccess(currentValue!, new Identifier(boundKey))));

            var restore = EnterElement(cursor, bitBase, map.DiffEntryBits, loop);
            EmitFieldDiffWrite(map.Value, baselineEntry, currentEntry, cursor, loopBody);
            restore();
        }

        cursor.Flush(loopBody);
        body.Add(new NumericForStatement(loop, _one, count, null, new Chunk(loopBody)));
    }
}
