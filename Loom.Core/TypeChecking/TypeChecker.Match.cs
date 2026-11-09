using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;
using Loom.Core.TypeChecking.Solving;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    public override Type VisitMatchExpression(MatchExpression matchExpression)
    {
        var scrutineeType = Visit(matchExpression.Expression);
        if (matchExpression.Arms.Count == 0)
            return BindType(matchExpression, PrimitiveType.Never);

        var armTypes = new List<Type>(matchExpression.Arms.Count);
        foreach (var arm in matchExpression.Arms)
            armTypes.Add(CheckMatchArm(arm, scrutineeType, null));

        CheckExhaustiveness(matchExpression, scrutineeType);

        return BindType(matchExpression, TypeSimplifier.Simplify(new UnionType(armTypes)));
    }

    /// <summary>
    ///     A match must either contain an irrefutable arm (a bare identifier, <c>let</c>, or wildcard
    ///     pattern with no guard) or, when the scrutinee is a union, cover every member of that union
    ///     across its arms - otherwise the compiled match can fall through and leave its result nil at
    ///     runtime. Non-union scrutinees fall back to requiring an irrefutable arm outright, since a
    ///     literal/typed pattern narrowing a single concrete type isn't the "exhaust a union" this
    ///     tracks, and guessing at that would make the check either too permissive or too strict.
    /// </summary>
    private void CheckExhaustiveness(MatchExpression matchExpression, Type scrutineeType)
    {
        if (matchExpression.Arms.Exists(IsIrrefutableArm))
            return;

        if (scrutineeType is UnionType union)
        {
            Type remaining = union;
            foreach (var arm in matchExpression.Arms)
            {
                if (arm.Guard != null) continue;

                remaining = RemoveArmCoverage(remaining, arm.Pattern);
                if (Type.IsNever(remaining))
                    return;
            }
        }

        _diagnostics.Error(
            matchExpression,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    private Type RemoveArmCoverage(Type remaining, Pattern pattern)
    {
        switch (pattern)
        {
            case WildcardPattern or IdentifierPattern or LetPattern:
                return PrimitiveType.Never;

            case OrPattern orPattern:
                foreach (var alternative in orPattern.Patterns)
                {
                    remaining = RemoveArmCoverage(remaining, alternative);
                    if (Type.IsNever(remaining))
                        break;
                }

                return remaining;

            case LiteralPattern literalPattern:
                return RemoveCoveredType(remaining, new LiteralType(literalPattern.Value));

            case QualifiedNamePattern qualifiedNamePattern:
                return RemoveCoveredType(remaining, _semanticModel.GetType(qualifiedNamePattern.Name));

            // An attached object sub-pattern with fields (e.g. `p when Point { x: 0 }`) only matches a
            // subset of the type, so it can't be treated as covering the whole pattern type like a bare
            // `p when Point` would - but an empty one (`p when Point { }`) imposes no such constraint, so
            // it covers exactly as much as no object sub-pattern at all.
            case TypedPattern { ObjectPattern: null or { Fields.Count: 0 } } typedPattern:
                return RemoveCoveredType(remaining, _semanticModel.GetType(typedPattern.Type));

            case TypePattern { ObjectPattern: null or { Fields.Count: 0 } } typePattern:
                return RemoveCoveredType(remaining, _semanticModel.GetType(typePattern.Type));

            default:
                return remaining;
        }
    }

    private static Type RemoveCoveredType(Type remaining, Type covered)
    {
        if (covered.IsAssignableTo(remaining) && remaining.IsAssignableTo(covered))
            return PrimitiveType.Never;

        if (remaining is not UnionType union)
            return remaining;

        var left = union.Types.FindAll(member => !member.IsAssignableTo(covered));
        return left.Count switch
        {
            0 => PrimitiveType.Never,
            1 => left[0],
            _ => TypeSimplifier.Simplify(new UnionType(left))
        };
    }

    private static bool IsIrrefutableArm(MatchArm arm) => arm.Guard == null && IsIrrefutablePattern(arm.Pattern);

    private static bool IsIrrefutablePattern(Pattern pattern) =>
        pattern switch
        {
            WildcardPattern or IdentifierPattern or LetPattern => true,
            OrPattern orPattern => orPattern.Patterns.Exists(IsIrrefutablePattern),
            _ => false
        };

    private Type CheckMatchArm(MatchArm matchArm, Type scrutineeType, Type? expected)
    {
        CheckPattern(matchArm.Pattern, scrutineeType);

        if (matchArm.Guard != null)
        {
            var guardType = Visit(matchArm.Guard, null);
            _semanticModel.TypeSolver.AddConstraint(guardType, PrimitiveType.Bool, matchArm.Guard);
        }

        if (expected == null)
            return Visit(matchArm.Body, null);

        var baseState = _flowAnalyzer.GetState(matchArm.Body);
        var armState = new FlowState(
            baseState.DefinitelyInitialized,
            baseState.MaybeInitialized,
            baseState.IsUnreachable,
            _flowState.NarrowedTypes
        );

        return Check(matchArm.Body, expected, armState);
    }

    private void CheckPattern(Pattern pattern, Type inputType)
    {
        switch (pattern)
        {
            case WildcardPattern wildcardPattern:
                BindType(wildcardPattern, inputType);
                break;
            case IdentifierPattern identifierPattern:
                BindType(identifierPattern, inputType);
                break;
            case LetPattern letPattern:
                BindType(letPattern, inputType);
                break;
            case LiteralPattern literalPattern:
                CheckLiteralPattern(literalPattern, inputType);
                break;
            case QualifiedNamePattern qualifiedNamePattern:
                CheckQualifiedNamePattern(qualifiedNamePattern, inputType);
                break;
            case RangePattern rangePattern:
                CheckRangePattern(rangePattern, inputType);
                break;
            case TypedPattern typedPattern:
                CheckTypedPattern(typedPattern, inputType);
                break;
            case TypePattern typePattern:
                CheckTypePattern(typePattern, inputType);
                break;
            case ObjectPattern objectPattern:
                CheckObjectPattern(objectPattern, inputType);
                break;
            case ArrayPattern arrayPattern:
                CheckArrayPattern(arrayPattern, inputType);
                break;
            case TuplePattern tuplePattern:
                CheckTuplePattern(tuplePattern, inputType);
                break;
            case OrPattern orPattern:
                CheckOrPattern(orPattern, inputType);
                break;
            case AndPattern andPattern:
                CheckAndPattern(andPattern, inputType);
                break;
            case NotPattern notPattern:
                CheckNotPattern(notPattern, inputType);
                break;
            case NullPattern nullPattern:
                BindType(nullPattern, PrimitiveType.Never);
                break;
        }
    }

    private void CheckAndPattern(AndPattern pattern, Type inputType)
    {
        CheckPattern(pattern.Pattern, inputType);

        var guardType = Visit(pattern.Guard, null);
        _semanticModel.TypeSolver.AddConstraint(guardType, PrimitiveType.Bool, pattern.Guard);

        BindType(pattern, _semanticModel.GetType(pattern.Pattern));
    }

    private void CheckNotPattern(NotPattern pattern, Type inputType)
    {
        CheckPattern(pattern.Pattern, inputType);
        BindType(pattern, RemoveCoveredType(inputType, _semanticModel.GetType(pattern.Pattern)));
    }

    private void CheckLiteralPattern(LiteralPattern pattern, Type inputType)
    {
        var literalType = new LiteralType(pattern.Value);
        if (!IsPatternCompatible(literalType, inputType))
            _diagnostics.Error(
                pattern,
                InternalCodes.TypeMismatch,
                $"Pattern of type '{literalType}' cannot match value of type '{inputType}'."
            );

        BindType(pattern, literalType);
    }

    /// <summary>
    ///     A qualified name pattern matches whatever it names, verbatim - visiting the wrapped
    ///     <see cref="QualifiedName" /> reuses the same reference resolution and member-access checking a
    ///     plain expression gets, so an unknown enum or an unknown member is already reported by the time
    ///     this runs. What is new here is requiring the answer be a compile-time constant: pattern
    ///     matching compiles to an equality comparison against a literal value (see
    ///     <c>LuauGenerator.CompilePattern</c>), which nothing else here promises.
    /// </summary>
    private void CheckQualifiedNamePattern(QualifiedNamePattern pattern, Type inputType)
    {
        var referencedType = Visit(pattern.Name);
        if (referencedType is not LiteralType literalType)
        {
            if (Type.IsNotUnknown(referencedType) && Type.IsNotNever(referencedType))
                _diagnostics.Error(
                    pattern,
                    InternalCodes.TypeMismatch,
                    $"'{pattern.Name}' cannot be used as a pattern because its value is not a compile-time constant."
                );

            BindType(pattern, PrimitiveType.Never);
            return;
        }

        if (!IsPatternCompatible(literalType, inputType))
            _diagnostics.Error(
                pattern,
                InternalCodes.TypeMismatch,
                $"Pattern of type '{literalType}' cannot match value of type '{inputType}'."
            );

        BindType(pattern, literalType);
    }

    private void CheckRangePattern(RangePattern pattern, Type inputType)
    {
        BindType(pattern.Minimum, PrimitiveType.Number);
        BindType(pattern.Maximum, PrimitiveType.Number);
        if (!IsPatternCompatible(PrimitiveType.Number, inputType))
            _diagnostics.Error(
                pattern,
                InternalCodes.TypeMismatch,
                $"Range pattern can only match values of type 'number', not '{inputType}'."
            );

        BindType(pattern, PrimitiveType.Number);
    }

    private void CheckTypedPattern(TypedPattern pattern, Type inputType)
    {
        var patternType = Visit(pattern.Type);
        if (!IsPatternCompatible(patternType, inputType))
            _diagnostics.Error(
                pattern,
                InternalCodes.TypeMismatch,
                $"Pattern of type '{patternType}' cannot match value of type '{inputType}'."
            );

        var matchedType = NarrowToType(inputType, patternType);
        BindType(pattern, matchedType);
        if (pattern.ObjectPattern != null)
            CheckObjectPattern(pattern.ObjectPattern, matchedType);
    }

    private void CheckTypePattern(TypePattern pattern, Type inputType)
    {
        var patternType = Visit(pattern.Type);
        if (!IsPatternCompatible(patternType, inputType))
            _diagnostics.Error(
                pattern,
                InternalCodes.TypeMismatch,
                $"Pattern of type '{patternType}' cannot match value of type '{inputType}'."
            );

        var matchedType = NarrowToType(inputType, patternType);
        BindType(pattern, matchedType);
        if (pattern.ObjectPattern != null)
            CheckObjectPattern(pattern.ObjectPattern, matchedType);
    }

    private void CheckObjectPattern(ObjectPattern pattern, Type inputType)
    {
        foreach (var field in pattern.Fields)
            CheckObjectPatternField(field, inputType);

        BindType(pattern, inputType);
    }

    private void CheckObjectPatternField(ObjectPatternField field, Type inputType)
    {
        var propertyType = TypeSimplifier.GetMemberPropertyType(inputType, field.Name.Text);
        if (propertyType == null)
        {
            if (Type.IsNotUnknown(inputType) && Type.IsNotNever(inputType))
                _diagnostics.Error(
                    field,
                    InternalCodes.InvalidAccess,
                    $"Property '{field.Name.Text}' does not exist on type '{inputType}'."
                );

            propertyType = PrimitiveType.Unknown;
        }

        CheckPattern(field.Pattern, propertyType);
    }

    private void CheckArrayPattern(ArrayPattern pattern, Type inputType)
    {
        var elementType = GetArrayElementType(inputType);
        if (elementType == null)
        {
            if (Type.IsNotUnknown(inputType) && Type.IsNotNever(inputType))
                _diagnostics.Error(
                    pattern,
                    InternalCodes.TypeMismatch,
                    $"Array pattern cannot match value of type '{inputType}'."
                );

            elementType = PrimitiveType.Unknown;
        }

        foreach (var element in pattern.Elements)
            CheckPattern(element, elementType);

        if (pattern.Rest != null)
            CheckRestPattern(pattern.Rest, elementType);

        BindType(pattern, inputType);
    }

    private void CheckTuplePattern(TuplePattern pattern, Type inputType)
    {
        if (inputType is Types.TupleType tupleType)
        {
            if (pattern.Patterns.Count != tupleType.ElementTypes.Count)
            {
                _diagnostics.Error(
                    pattern,
                    InternalCodes.TupleArityMismatch,
                    $"Tuple type '{tupleType}' expects {tupleType.ElementTypes.Count} element(s), but {pattern.Patterns.Count} were provided."
                );

                CheckTuplePatternElementsAsUnknown(pattern, inputType);
                return;
            }

            CheckTuplePatternElements(pattern, inputType, tupleType.ElementTypes);
            return;
        }

        var elementTypes = GetTupleElementTypes(inputType, pattern.Patterns.Count);
        if (elementTypes == null)
        {
            if (Type.IsNotUnknown(inputType) && Type.IsNotNever(inputType))
                _diagnostics.Error(
                    pattern,
                    InternalCodes.TypeMismatch,
                    $"Tuple pattern cannot match value of type '{inputType}'."
                );

            CheckTuplePatternElementsAsUnknown(pattern, inputType);
            return;
        }

        CheckTuplePatternElements(pattern, inputType, elementTypes);
    }

    private void CheckTuplePatternElements(TuplePattern pattern, Type inputType, List<Type> elementTypes)
    {
        for (var i = 0; i < pattern.Patterns.Count; i++)
            CheckPattern(pattern.Patterns[i], elementTypes[i]);

        BindType(pattern, inputType);
    }

    private void CheckTuplePatternElementsAsUnknown(TuplePattern pattern, Type inputType)
    {
        foreach (var element in pattern.Patterns)
            CheckPattern(element, PrimitiveType.Unknown);

        BindType(pattern, inputType);
    }

    // A union of tuples narrows per-position across every member matching the pattern's arity, mirroring
    // GetArrayElementType's handling of unions of arrays.
    private static List<Type>? GetTupleElementTypes(Type type, int arity)
    {
        if (type is Types.TupleType tuple)
            return tuple.ElementTypes.Count == arity ? tuple.ElementTypes : null;

        if (type is not UnionType union)
            return null;

        var perPosition = new List<Type>[arity];
        for (var i = 0; i < arity; i++)
            perPosition[i] = [];

        var matchedAny = false;
        foreach (var member in union.Types)
        {
            var memberElementTypes = GetTupleElementTypes(member, arity);
            if (memberElementTypes == null)
                continue;

            matchedAny = true;
            for (var i = 0; i < arity; i++)
                perPosition[i].Add(memberElementTypes[i]);
        }

        return matchedAny ? [.. perPosition.Select(types => TypeSimplifier.Simplify(new UnionType(types)))] : null;
    }

    private void CheckRestPattern(RestPattern pattern, Type elementType)
    {
        var arrayType = new ArrayType(elementType, false);
        CheckPattern(pattern.Pattern, arrayType);
        BindType(pattern, arrayType);
    }

    private void CheckOrPattern(OrPattern pattern, Type inputType)
    {
        foreach (var alternative in pattern.Patterns)
            CheckPattern(alternative, inputType);

        BindType(pattern, inputType);
    }

    private static Type NarrowToType(Type inputType, Type patternType)
    {
        if (inputType is UnionType union)
        {
            var members = union.Types.FindAll(member => member.IsAssignableTo(patternType));
            if (members.Count > 0)
                return TypeSimplifier.Simplify(new UnionType(members));
        }

        return patternType;
    }

    // A union of arrays narrows to the union of each member's element type - unless some member isn't
    // array-like at all, in which case the whole union can't be array-destructured.
    private static Type? GetArrayElementType(Type type)
    {
        if (type is InstantiatedType instantiated)
            type = instantiated.Expand();

        if (type is ArrayType array)
            return array.ElementType;

        if (type is not UnionType union)
            return null;

        var elementTypes = new List<Type>(union.Types.Count);
        foreach (var member in union.Types)
        {
            var elementType = GetArrayElementType(member);
            if (elementType == null)
                return null;

            elementTypes.Add(elementType);
        }

        return TypeSimplifier.Simplify(new UnionType(elementTypes));
    }

    /// <summary>
    ///     A pattern only has to be able to match <em>some</em> value the scrutinee can hold, so both
    ///     sides are widened first: matching a literal scrutinee like `match 1` against `0` is a normal
    ///     (if never-taken) arm rather than a type error, while `match "hi"` against `0` still fails
    ///     because no widening makes a number and a string overlap.
    /// </summary>
    private static bool IsPatternCompatible(Type patternType, Type inputType)
    {
        if (Type.IsUnknown(inputType) || Type.IsNever(inputType))
            return true;

        var widenedPattern = patternType.Widen();
        var widenedInput = inputType.Widen();
        return widenedPattern.IsAssignableTo(widenedInput) || widenedInput.IsAssignableTo(widenedPattern);
    }
}
