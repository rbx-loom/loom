using Loom.Core.TypeChecking.Serialization;
using Loom.Luau;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Serialization;

/// <summary>Sizing: the byte width of a value, folded to an expression where it can be and accumulated in a local where it cannot.</summary>
internal sealed partial class SerializationEmitter
{
    /// <summary>
    ///     Computes the total width, appending statements to <paramref name="body" /> only when some field
    ///     cannot state its contribution as an expression. A fixed schema folds to a literal, widths that
    ///     are inline-expressible stay in one arithmetic expression, and anything needing control flow -
    ///     an array of variable-width elements, a union whose width follows its tag - falls back to
    ///     accumulating into a local.
    /// </summary>
    private LuauExpression EmitSizeComputation(List<LuauStatement> body)
    {
        if (schema.FixedByteCount is { } fixedByteCount)
            return new NumberLiteral(fixedByteCount);

        var constant = schema.HeaderBytes + schema.Fields.Sum(f => f.BodyBytes ?? 0);
        LuauExpression? inline = constant > 0 ? new NumberLiteral(constant) : null;
        var traversal = new List<LuauStatement>();
        _measureRoot = traversal;
        foreach (var serializationField in schema.Fields)
        {
            var value = Access(new Identifier(ValueParameter), serializationField.Path);
            if (InlineContribution(serializationField, value) is { } contribution)
            {
                inline = inline == null ? contribution : Add(inline, contribution);
                continue;
            }

            if (serializationField.BodyBytes != null)
                continue;

            EmitMeasure(serializationField, value, traversal);
        }

        if (traversal.Count == 0)
            return inline ?? _zero;

        body.Add(new LocalVariable(SizeLocal, null, inline ?? _zero));
        body.AddRange(traversal);

        return new Identifier(SizeLocal);
    }

    /// <summary>A field's contribution as an expression, or null when it needs the traversal.</summary>
    private static LuauExpression? InlineContribution(SerializationField serializationField, LuauExpression value)
    {
        if (SentinelNamesOf(serializationField) is { Count: > 0 })
            return new IfExpression(
                new BinaryOperator(new Identifier(SentinelIndexLocal(serializationField.Path)), "==", _zero),
                new NumberLiteral(SentinelComponentBytes(serializationField)),
                [],
                _zero
            );

        return serializationField switch
        {
            StringField stringField => Add(new NumberLiteral(stringField.LengthType.ByteCount()), Length(value)),
            ArrayField { Element.BodyBytes: { } elementBytes } arrayField =>
                Add(
                    Add(new NumberLiteral(arrayField.LengthType.ByteCount()), Multiply(Length(value), new NumberLiteral(elementBytes))),
                    ElementBitBlockSize(arrayField.Element.HeaderBits, Length(value))
                ),
            OptionalField { Inner.BodyBytes: 0 } => _zero,
            OptionalField { Inner.BodyBytes: { } innerBytes } =>
                new IfExpression(IsPresent(value), new NumberLiteral(innerBytes), [], _zero),
            _ => null
        };
    }

    /// <summary>Accumulates a field's width into the size local, for shapes that need control flow.</summary>
    private void EmitMeasure(SerializationField serializationField, LuauExpression value, List<LuauStatement> statements)
    {
        switch (serializationField)
        {
            case TupleField tupleField:
                foreach (var (part, partValue) in ChildrenOf(tupleField, value))
                    MeasureField(part, partValue, statements);

                return;

            case OptionalField optionalField:
                MeasureOptional(optionalField, value, statements);
                return;

            case UnionField unionField:
                MeasureUnion(unionField, value, statements);
                return;

            case MapField mapField:
                MeasureMap(mapField, value, statements);
                return;

            case ArrayField arrayField:
                MeasureArray(arrayField, value, statements);
                return;
        }
    }

    private void MeasureOptional(OptionalField optionalField, LuauExpression value, List<LuauStatement> statements)
    {
        var innerStatements = new List<LuauStatement>();
        foreach (var (inner, innerValue) in ChildrenOf(optionalField, value))
            MeasureField(inner, innerValue, innerStatements);

        if (innerStatements.Count > 0)
            statements.Add(new IfStatement(IsPresent(value), new Chunk(innerStatements), [], null));
    }

    private void MeasureUnion(UnionField unionField, LuauExpression value, List<LuauStatement> statements)
    {
        var tag = UnionTag(unionField, value, statements);
        var chain = new IfChain(statements);
        for (var index = 0; index < unionField.Variants.Count; index++)
        {
            if (VariantSizeExpression(unionField, unionField.Variants[index], value) is not { } variantSize)
                continue;

            var addStatements = new List<LuauStatement>();
            AddToSize(addStatements, variantSize);
            chain.Add(new BinaryOperator(tag, "==", new NumberLiteral(index)), new Chunk(addStatements));
        }
    }

    private void MeasureMap(MapField mapField, LuauExpression value, List<LuauStatement> statements)
    {
        AddToSize(statements, new NumberLiteral(mapField.LengthType.ByteCount()));

        var countLocal = ReserveLocal(LeafName(mapField.Path) + "_count");
        statements.Add(new LocalVariable(countLocal, null, _zero));

        using (LoopScope())
        {
            var keyLocal = ReserveLocal(LeafName(mapField.Key.Path));
            var valueLocal = ReserveLocal(LeafName(mapField.Value.Path));
            var pairStatements = new List<LuauStatement> { new ExpressionStatement(new BinaryOperator(new Identifier(countLocal), "+=", _one)) };

            MeasureField(mapField.Key, new Identifier(keyLocal), pairStatements);
            MeasureField(mapField.Value, new Identifier(valueLocal), pairStatements);

            statements.Add(new ForStatement([keyLocal, valueLocal], value, new Chunk(pairStatements)));
        }

        AddToSize(statements, ElementBitBlockSize(mapField.EntryBits, new Identifier(countLocal)));

        if (ReferenceEquals(statements, _measureRoot))
            _boundCounts[mapField] = countLocal;
    }

    private void MeasureArray(ArrayField arrayField, LuauExpression value, List<LuauStatement> statements)
    {
        AddToSize(statements, new NumberLiteral(arrayField.LengthType.ByteCount()));

        var arrayCount = BindCount(value, LeafName(arrayField.Path), statements);
        AddToSize(statements, ElementBitBlockSize(arrayField.Element.HeaderBits, arrayCount));

        if (ReferenceEquals(statements, _measureRoot))
            _boundCounts[arrayField] = arrayCount.Name;

        using var scope = LoopScope();
        var loop = ReserveLocal(LoopLocal);

        var element = new Identifier(ReserveLocal(LeafName(arrayField.Element.Path)));
        var elementStatements = new List<LuauStatement>();
        MeasureField(arrayField.Element, element, elementStatements);

        if (elementStatements.Count == 0)
            return;

        var bind = new ConstVariable(element.Name, null, new ElementAccess(value, new Identifier(loop)));
        statements.Add(new NumericForStatement(loop, _one, arrayCount, null, new Chunk([bind, ..elementStatements])));
    }

    /// <summary>
    ///     The entry count for a map's length prefix: the size pass's own count when it ran in the scope
    ///     the write lives in, and a counting loop otherwise.
    /// </summary>
    private Identifier MapCount(MapField mapField, string leaf, LuauExpression value, List<LuauStatement> body)
    {
        if (_boundCounts.TryGetValue(mapField, out var measured))
            return new Identifier(measured);

        var count = new Identifier(ReserveLocal(leaf + "_count"));
        body.Add(new LocalVariable(count.Name, null, _zero));

        using var scope = LoopScope();
        var key = ReserveLocal(LeafName(mapField.Key.Path));
        body.Add(new ForStatement([key], value, new Chunk([new ExpressionStatement(new BinaryOperator(count, "+=", _one))])));

        return count;
    }

    /// <summary>
    ///     The entry count for an array, reusing the size pass's when that ran in the scope the caller
    ///     lives in. Unlike a map an array can simply be measured, so the fallback is the length operator
    ///     rather than a loop - but the answer is still worth a name, since every caller wants it twice.
    /// </summary>
    private Identifier ArrayCount(ArrayField arrayField, string leaf, LuauExpression value, List<LuauStatement> body) =>
        _boundCounts.TryGetValue(arrayField, out var measured)
            ? new Identifier(measured)
            : BindCount(value, leaf, body);

    /// <summary>Names a collection's length, so reaching for it again costs a register rather than a walk.</summary>
    private Identifier BindCount(LuauExpression value, string leaf, List<LuauStatement> body)
    {
        var count = new Identifier(ReserveLocal(leaf + "_count"));
        body.Add(new ConstVariable(count.Name, null, Length(value)));

        return count;
    }

    /// <summary>Adds one field's width, inline when it can be stated as an expression.</summary>
    private void MeasureField(SerializationField serializationField, LuauExpression value, List<LuauStatement> statements)
    {
        if (InlineContribution(serializationField, value) is { } contribution)
        {
            AddToSize(statements, contribution);
            return;
        }

        if (serializationField.BodyBytes is { } fixedBytes)
        {
            if (fixedBytes > 0)
                AddToSize(statements, new NumberLiteral(fixedBytes));

            return;
        }

        EmitMeasure(serializationField, value, statements);
    }

    /// <summary>Adds to the running size, emitting nothing at all when the amount folds to zero.</summary>
    private static void AddToSize(List<LuauStatement> statements, LuauExpression amount)
    {
        if (amount is NumberLiteral { Value: 0 })
            return;

        statements.Add(new ExpressionStatement(new BinaryOperator(new Identifier(SizeLocal), "+=", amount)));
    }
}
