using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using Identifier = Loom.Luau.AST.Identifier;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using PrimitiveTypeKind = Loom.Core.TypeChecking.Types.PrimitiveTypeKind;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    // Set while compiling a guard, so it sees the pattern's bound names as subject accesses instead of
    // the not-yet-declared locals (those are only introduced in the arm body, after the guard passes).
    private Dictionary<string, LuauExpression>? _guardSubstitutions;

    public override LuauNode VisitMatchExpression(MatchExpression matchExpression)
    {
        if (matchExpression.Arms.Count == 0)
            return new NilLiteral();

        // A match with only a single, unguarded wildcard arm always takes that arm no matter what the
        // scrutinee is, so the subject/local-match scaffolding - and evaluating the scrutinee at all -
        // can be skipped entirely in favor of just the arm's body.
        if (matchExpression.Arms is [{ Pattern: WildcardPattern, Guard: null } soleArm])
            return Visit(soleArm.Body);

        var subject = _state.PushToVariable("_subject", Visit(matchExpression.Expression));
        var matchName = _state.Scope.AddIdentifier("_match");
        var matchIdentifier = new Identifier(matchName);
        _state.Prereq(new LocalVariable(matchName, null, null));

        LuauExpression? thenCondition = null;
        Chunk? thenBranch = null;
        var elseIfBranches = new List<ElseIfBranch>();
        Chunk? elseBranch = null;

        foreach (var arm in matchExpression.Arms)
        {
            var conditions = new List<LuauExpression>();
            var bindings = new List<LuauStatement>();
            var shape = CompilePattern(arm.Pattern, subject, conditions, bindings);
            if (shape == PatternShape.Unsupported)
                continue;

            var isIrrefutable = shape == PatternShape.Irrefutable;

            if (arm.Guard != null)
            {
                conditions.Add(CompileGuard(arm.Pattern, arm.Guard, subject));
                isIrrefutable = false;
            }

            var armChunk = CompileMatchArmBody(arm, matchIdentifier, bindings);
            if (isIrrefutable)
            {
                elseBranch = armChunk;
                break;
            }

            var condition = CombineWith(conditions, "and");
            if (thenCondition == null)
            {
                thenCondition = condition;
                thenBranch = armChunk;
            }
            else
            {
                elseIfBranches.Add(new ElseIfBranch(condition, armChunk));
            }
        }

        if (thenCondition == null)
        {
            if (elseBranch != null)
                _state.Prereq([..elseBranch.Statements]);
        }
        else
        {
            _state.Prereq(new IfStatement(thenCondition, thenBranch!, elseIfBranches, elseBranch));
        }

        return matchIdentifier;
    }

    private LuauExpression CompileGuard(Pattern pattern, Expression guard, LuauExpression subject)
    {
        var substitutions = CollectPatternBindings(pattern, subject).ToDictionary(binding => binding.Name, binding => binding.Value);
        if (substitutions.Count == 0)
            return Visit(guard);

        var previous = _guardSubstitutions;
        _guardSubstitutions = substitutions;
        try
        {
            return Visit(guard);
        }
        finally
        {
            _guardSubstitutions = previous;
        }
    }

    private Chunk CompileMatchArmBody(MatchArm arm, Identifier matchIdentifier, List<LuauStatement> bindings)
    {
        var statements = new List<LuauStatement>();
        var (body, scope) = _state.Capture(() =>
            {
                foreach (var binding in bindings)
                    _state.Prereq(binding);

                return Visit(arm.Body);
            }
        );

        ApplyPrereqAndPostreq(
            statements,
            scope,
            new ExpressionStatement(new BinaryOperator(matchIdentifier, "=", body))
        );

        return new Chunk(statements);
    }

    private static LuauExpression CombineWith(List<LuauExpression> conditions, string @operator) =>
        conditions.Count switch
        {
            0 => new BooleanLiteral(true),
            _ => conditions.Skip(1).Aggregate(conditions[0], (accumulated, next) => new BinaryOperator(accumulated, @operator, next))
        };

    // A single equality comparison flips in place (`typeof(x) == "string"` -> `typeof(x) ~= "string"`);
    // anything else - a compound and/or condition, a bare call like `x:IsA(...)` - is wrapped in `not (...)`
    // rather than distributed via De Morgan's, since the AST here doesn't track operator precedence.
    private static LuauExpression NegateCondition(LuauExpression condition) =>
        condition switch
        {
            BinaryOperator { Operator: "==" } binary => new BinaryOperator(binary.Left, "~=", binary.Right),
            BinaryOperator { Operator: "~=" } binary => new BinaryOperator(binary.Left, "==", binary.Right),
            BinaryOperator => new Luau.AST.UnaryOperator("not ", new Luau.AST.Parenthesized(condition)),
            _ => new Luau.AST.UnaryOperator("not ", condition)
        };
}