using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using Identifier = Loom.Luau.AST.Identifier;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using PrimitiveTypeKind = Loom.Core.TypeChecking.Types.PrimitiveTypeKind;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    private enum PatternShape
    {
        Unsupported,
        Refutable,
        Irrefutable
    }

    /// <summary>
    ///     Mirrors the binding side of <see cref="CompilePattern" /> - without any of the runtime
    ///     conditions - so a guard can reference names a compound pattern (array/object/tuple/typed/etc.)
    ///     would bind, before those bindings are actually declared as locals in the arm body.
    /// </summary>
    private IEnumerable<(string Name, LuauExpression Value)> CollectPatternBindings(Pattern pattern, LuauExpression subject)
    {
        switch (pattern)
        {
            case IdentifierPattern identifierPattern:
                yield return (identifierPattern.Name.Text, subject);

                break;

            case LetPattern letPattern:
                yield return (letPattern.Name.Text, subject);

                break;

            case TypedPattern typedPattern:
                yield return (typedPattern.Name.Text, subject);
                if (typedPattern.ObjectPattern != null)
                    foreach (var binding in CollectObjectPatternBindings(typedPattern.ObjectPattern, subject))
                        yield return binding;

                break;

            case TypePattern { ObjectPattern: not null } typePattern:
                foreach (var binding in CollectObjectPatternBindings(typePattern.ObjectPattern, subject))
                    yield return binding;

                break;

            case ArrayPattern arrayPattern:
                foreach (var binding in CollectElementBindings(arrayPattern.Elements, subject))
                    yield return binding;

                if (arrayPattern.Rest != null)
                    foreach (var binding in CollectPatternBindings(arrayPattern.Rest.Pattern, BuildArrayRestSlice(subject, arrayPattern.Elements.Count)))
                        yield return binding;

                break;

            case ObjectPattern objectPattern:
                foreach (var binding in CollectObjectPatternBindings(objectPattern, subject))
                    yield return binding;

                break;

            case TuplePattern tuplePattern:
                foreach (var binding in CollectElementBindings(tuplePattern.Patterns, subject))
                    yield return binding;

                break;

            case AndPattern andPattern:
                foreach (var binding in CollectPatternBindings(andPattern.Pattern, subject))
                    yield return binding;

                break;

            // Every alternative binds the same names against the same subject, so any one will do.
            case OrPattern { Patterns: [var first, ..] }:
                foreach (var binding in CollectPatternBindings(first, subject))
                    yield return binding;

                break;
        }
    }

    private IEnumerable<(string Name, LuauExpression Value)> CollectElementBindings(IReadOnlyList<Pattern> elements, LuauExpression subject)
    {
        for (var i = 0; i < elements.Count; i++)
            foreach (var binding in CollectPatternBindings(elements[i], ElementAt(subject, i)))
                yield return binding;
    }

    private IEnumerable<(string Name, LuauExpression Value)> CollectObjectPatternBindings(ObjectPattern objectPattern, LuauExpression subject)
    {
        var patternType = _semanticModel.GetType(objectPattern);
        foreach (var field in objectPattern.Fields)
        {
            var name = GetRenamedPatternFieldName(patternType, field.Name.Text);
            var propertyAccess = new Luau.AST.PropertyAccess(subject, [name]);
            foreach (var binding in CollectPatternBindings(field.Pattern, propertyAccess))
                yield return binding;
        }
    }

    /// <summary>
    ///     Compiles a pattern against <paramref name="subject" />, appending any runtime checks the
    ///     pattern requires to <paramref name="conditions" /> (AND-ed together by the caller) and any
    ///     variable bindings the pattern introduces to <paramref name="bindings" /> (emitted inside the
    ///     arm body, after the conditions have already passed). Reports a diagnostic and answers
    ///     <see cref="PatternShape.Unsupported" /> for pattern kinds that still aren't supported, so the
    ///     caller can skip the arm.
    /// </summary>
    private PatternShape CompilePattern(Pattern pattern, LuauExpression subject, List<LuauExpression> conditions, List<LuauStatement> bindings)
    {
        switch (pattern)
        {
            case WildcardPattern:
                return PatternShape.Irrefutable;

            case IdentifierPattern identifierPattern:
                return BindWhole(identifierPattern.Name.Text, subject, bindings);

            case LetPattern letPattern:
                return BindWhole(letPattern.Name.Text, subject, bindings);

            case LiteralPattern literalPattern:
                return Require(conditions, EqualTo(subject, LiteralValueToExpression(literalPattern.Value)));

            case QualifiedNamePattern qualifiedNamePattern:
                return CompileQualifiedNamePattern(qualifiedNamePattern, subject, conditions);

            case NullPattern:
                return Require(conditions, EqualTo(subject, new NilLiteral()));

            case RangePattern rangePattern:
                return CompileRangePattern(rangePattern, subject, conditions);

            case TypedPattern typedPattern:
                AddTypeCondition(conditions, _semanticModel.GetType(typedPattern.Type), subject, typedPattern.ObjectPattern == null);
                bindings.Add(new ConstVariable(typedPattern.Name.Text, null, subject));

                return RefutableIf(CompileOptionalObjectPattern(typedPattern.ObjectPattern, subject, conditions, bindings));

            case TypePattern typePattern:
                AddTypeCondition(conditions, _semanticModel.GetType(typePattern.Type), subject, typePattern.ObjectPattern == null);

                return RefutableIf(CompileOptionalObjectPattern(typePattern.ObjectPattern, subject, conditions, bindings));

            case ArrayPattern arrayPattern:
                return CompileArrayPattern(arrayPattern, subject, conditions, bindings);

            case ObjectPattern objectPattern:
                conditions.Add(RequireTable(subject));

                return RefutableIf(CompileObjectPatternFields(objectPattern, subject, conditions, bindings));

            case TuplePattern tuplePattern:
                conditions.Add(RequireTable(subject));

                return RefutableIf(CompileElementPatterns(tuplePattern.Patterns, subject, conditions, bindings));

            case OrPattern orPattern:
                return CompileOrPattern(orPattern, subject, conditions, bindings);

            case AndPattern andPattern:
                if (CompilePattern(andPattern.Pattern, subject, conditions, bindings) == PatternShape.Unsupported)
                    return PatternShape.Unsupported;

                return Require(conditions, CompileGuard(andPattern.Pattern, andPattern.Guard, subject));

            // Bindings from the inner pattern are discarded - nothing is captured when a negated pattern
            // doesn't hold, so there is nothing meaningful for the arm/condition after it to reference.
            case NotPattern notPattern:
                var innerConditions = new List<LuauExpression>();
                if (CompilePattern(notPattern.Pattern, subject, innerConditions, []) == PatternShape.Unsupported)
                    return PatternShape.Unsupported;

                return Require(conditions, NegateCondition(CombineWith(innerConditions, "and")));

            default:
                _diagnostics.NotImplemented(pattern, $"Pattern kind '{pattern.GetType().Name}' is not yet supported in code generation.");

                return PatternShape.Unsupported;
        }
    }

    private static PatternShape BindWhole(string name, LuauExpression subject, List<LuauStatement> bindings)
    {
        bindings.Add(new ConstVariable(name, null, subject));

        return PatternShape.Irrefutable;
    }

    private static PatternShape Require(List<LuauExpression> conditions, LuauExpression condition)
    {
        conditions.Add(condition);

        return PatternShape.Refutable;
    }

    private static PatternShape RefutableIf(bool compiled) => compiled ? PatternShape.Refutable : PatternShape.Unsupported;

    private static BinaryOperator EqualTo(LuauExpression subject, LuauExpression value) => new(subject, "==", value);

    private static BinaryOperator RequireTable(LuauExpression subject) => EqualTo(TypeofCall(subject), new StringLiteral("table"));

    private static Luau.AST.ElementAccess ElementAt(LuauExpression subject, int zeroBasedIndex) => new(subject, new NumberLiteral(zeroBasedIndex + 1));

    // Resolves to the same LiteralType a literal pattern would when CheckQualifiedNamePattern
    // accepted it, so it compiles to the identical comparison - the value just came from a name
    // instead of being spelled out. An unresolved name or a non-constant reference already failed
    // type checking and bound something else (never, most often); this only has to not crash on
    // its way to a compile the diagnostic already fails.
    private PatternShape CompileQualifiedNamePattern(QualifiedNamePattern qualifiedNamePattern, LuauExpression subject, List<LuauExpression> conditions) =>
        _semanticModel.GetType(qualifiedNamePattern) is LiteralType literalType
            ? Require(conditions, EqualTo(subject, LiteralValueToExpression(literalType.Value)))
            : PatternShape.Unsupported;

    private static PatternShape CompileRangePattern(RangePattern rangePattern, LuauExpression subject, List<LuauExpression> conditions)
    {
        AddTypeofCondition(conditions, PrimitiveType.Number, subject);
        if (rangePattern.Minimum is LiteralPattern minimum)
            conditions.Add(new BinaryOperator(subject, ">=", LiteralValueToExpression(minimum.Value)));

        if (rangePattern.Maximum is LiteralPattern maximum)
            conditions.Add(new BinaryOperator(subject, "<=", LiteralValueToExpression(maximum.Value)));

        return PatternShape.Refutable;
    }

    private PatternShape CompileArrayPattern(ArrayPattern arrayPattern, LuauExpression subject, List<LuauExpression> conditions, List<LuauStatement> bindings)
    {
        conditions.Add(RequireTable(subject));
        var compiled = CompileElementPatterns(arrayPattern.Elements, subject, conditions, bindings)
                       && (arrayPattern.Rest == null
                           || CompilePattern(arrayPattern.Rest.Pattern, BuildArrayRestSlice(subject, arrayPattern.Elements.Count), conditions, bindings) != PatternShape.Unsupported);

        return RefutableIf(compiled);
    }

    private bool CompileElementPatterns(IReadOnlyList<Pattern> elements, LuauExpression subject, List<LuauExpression> conditions, List<LuauStatement> bindings)
    {
        for (var i = 0; i < elements.Count; i++)
            if (CompilePattern(elements[i], ElementAt(subject, i), conditions, bindings) == PatternShape.Unsupported)
                return false;

        return true;
    }

    private PatternShape CompileOrPattern(OrPattern orPattern, LuauExpression subject, List<LuauExpression> conditions, List<LuauStatement> bindings)
    {
        // The resolver requires every alternative to bind the same names (Resolver.Patterns.cs
        // VisitOrPattern), so the first alternative's bindings stand in for whichever one matches.
        var alternativeConditions = new List<LuauExpression>();
        List<LuauStatement>? representativeBindings = null;
        foreach (var alternative in orPattern.Patterns)
        {
            var alternativeCondition = new List<LuauExpression>();
            var alternativeBindings = new List<LuauStatement>();
            var shape = CompilePattern(alternative, subject, alternativeCondition, alternativeBindings);
            if (shape == PatternShape.Unsupported)
                return PatternShape.Unsupported;

            representativeBindings ??= alternativeBindings;
            if (shape == PatternShape.Irrefutable)
            {
                bindings.AddRange(representativeBindings);

                return PatternShape.Irrefutable;
            }

            alternativeConditions.Add(CombineWith(alternativeCondition, "and"));
        }

        bindings.AddRange(representativeBindings!);

        return Require(conditions, CombineWith(alternativeConditions, "or"));
    }

    private bool CompileOptionalObjectPattern(ObjectPattern? objectPattern, LuauExpression subject, List<LuauExpression> conditions, List<LuauStatement> bindings) =>
        objectPattern == null || CompileObjectPatternFields(objectPattern, subject, conditions, bindings);

    private bool CompileObjectPatternFields(ObjectPattern objectPattern, LuauExpression subject, List<LuauExpression> conditions, List<LuauStatement> bindings)
    {
        var patternType = _semanticModel.GetType(objectPattern);
        foreach (var field in objectPattern.Fields)
        {
            var name = GetRenamedPatternFieldName(patternType, field.Name.Text);
            var propertyAccess = new Luau.AST.PropertyAccess(subject, [name]);
            if (CompilePattern(field.Pattern, propertyAccess, conditions, bindings) == PatternShape.Unsupported)
                return false;
        }

        return true;
    }

    private string GetRenamedPatternFieldName(Type patternType, string name) =>
        _semanticModel.GetPropertySymbol(patternType, [name]) is { } propertySymbol
        && propertySymbol.TryGetIntrinsicAttribute("luau_name", out var luauNameAttribute)
        && ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral)
            ? nameLiteral.Value
            : name;

    private static Call BuildArrayRestSlice(LuauExpression subject, int elementCount)
    {
        var length = new Luau.AST.UnaryOperator("#", subject);
        return new Call(
            new Luau.AST.PropertyAccess(new Identifier("table"), ["move"]),
            [subject, new NumberLiteral(elementCount + 1), length, new NumberLiteral(1), new Table([])]
        );
    }

    private static void AddTypeofCondition(List<LuauExpression> conditions, Type type, LuauExpression subject)
    {
        var typeofString = GetLuauTypeofString(type);
        if (typeofString != null)
            conditions.Add(EqualTo(TypeofCall(subject), new StringLiteral(typeofString)));
    }

    private static List<LuauExpression> BuildTypeCondition(Type type, LuauExpression subject, bool checkRequiredFields)
    {
        var conditions = new List<LuauExpression>();
        AddTypeCondition(conditions, type, subject, checkRequiredFields);
        return conditions;
    }

    // `n when Foo` needs more than `typeof(n) == "table"`: a Roblox Instance subclass is checked with
    // `:IsA(...)`, and a plain interface has no runtime representation, so matching one structurally
    // requires checking each of its required fields exists.
    private static void AddTypeCondition(List<LuauExpression> conditions, Type type, LuauExpression subject, bool checkRequiredFields)
    {
        if (type is InstantiatedType instantiated)
            type = instantiated.Expand();

        if (type is UnionType union)
        {
            conditions.Add(CombineWith(union.Types.ConvertAll(member => CombineWith(BuildTypeCondition(member, subject, checkRequiredFields), "and")), "or"));
            return;
        }

        if (type is IntersectionType intersection)
        {
            conditions.AddRange(intersection.Types.SelectMany(member => BuildTypeCondition(member, subject, checkRequiredFields)));
            return;
        }

        if (type is not InterfaceType interfaceType)
        {
            AddTypeofCondition(conditions, type, subject);
            return;
        }

        if (interfaceType.MatchOrMatchConstraint(i => i.Name is "Instance" or "Object"))
        {
            conditions.Add(EqualTo(TypeofCall(subject), new StringLiteral("Instance")));
            conditions.Add(new Call(new Luau.AST.PropertyAccess(subject, ["IsA"]), [new StringLiteral(interfaceType.Name)], true));
            return;
        }

        conditions.Add(RequireTable(subject));
        if (!checkRequiredFields)
            return;

        foreach (var property in interfaceType.Properties.Where(property => Type.IsNotOptional(property.ValueType)))
            conditions.Add(new BinaryOperator(new Luau.AST.PropertyAccess(subject, [property.Name]), "~=", new NilLiteral()));
    }

    private static Call TypeofCall(LuauExpression subject) => new(new Identifier("typeof"), [subject]);

    private static string? GetLuauTypeofString(Type type) =>
        type switch
        {
            PrimitiveType { Kind: PrimitiveTypeKind.Number } => "number",
            PrimitiveType { Kind: PrimitiveTypeKind.String } => "string",
            PrimitiveType { Kind: PrimitiveTypeKind.Bool } => "boolean",
            LiteralType { Value: long or int or double } => "number",
            LiteralType { Value: string } => "string",
            LiteralType { Value: bool } => "boolean",
            FunctionType => "function",
            NativelyIndexableType => "table",
            _ => null
        };
}
