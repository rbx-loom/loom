using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Generation.Macros;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using Attribute = Loom.Core.Parsing.AST.Attribute;
using BinaryOperator = Loom.Core.Parsing.AST.BinaryOperator;
using ElementAccess = Loom.Core.Parsing.AST.ElementAccess;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Core.Parsing.AST.Identifier;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using Parameter = Loom.Luau.AST.Parameter;
using Parenthesized = Loom.Core.Parsing.AST.Parenthesized;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using PropertyAccess = Loom.Core.Parsing.AST.PropertyAccess;
using UnaryOperator = Loom.Core.Parsing.AST.UnaryOperator;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitAttribute(Attribute attribute) => attribute.IsInvoked ? VisitInvocation(attribute) : Visit(attribute.Expression);

    public override LuauNode VisitInvocation(Invocation invocation)
    {
        // Ahead of the receiver being visited, so a chain of array combinators is still a chain rather
        // than a loop that has already been emitted - see ArrayPipeline.
        if (_arrayPipeline.TryGenerate(invocation, out var fused))
            return fused;

        var arguments = GenerateArguments(invocation);
        return invocation.Expression switch
        {
            QualifiedName { Names: var names } qualifiedName when names.Exists(n => n.IsOptional) => GenerateOptionalChain(
                qualifiedName,
                Visit(qualifiedName.Identifier),
                names,
                member => BuildCallExpression(invocation, member, arguments)
            ),
            PropertyAccess { Names: var names } propertyAccess when names.Exists(n => n.IsOptional) => GenerateOptionalChain(
                propertyAccess,
                Visit(propertyAccess.Expression),
                names,
                member => BuildCallExpression(invocation, member, arguments)
            ),
            ElementAccess { IsOptional: true } elementAccess => GenerateOptionalElementAccess(
                elementAccess,
                Visit(elementAccess.Expression),
                Visit(elementAccess.IndexExpression),
                member => BuildCallExpression(invocation, member, arguments)
            ),
            _ => BuildCallExpression(invocation, Visit(invocation.Expression), arguments)
        };
    }

    private LuauExpression BuildCallExpression(Invocation invocation, LuauExpression callee, List<LuauExpression> arguments)
    {
        if (_semanticModel.GetType(invocation.Expression) is InstantiatedType { GenericType.UnderlyingType: InterfaceType { Name: "Event" } })
            return new Call(new Luau.AST.PropertyAccess(callee, ["Fire"]), arguments, true);

        var wrapsErrors = _semanticModel.TryGetIntrinsicAttribute(invocation.Expression, "wraps_errors", out _);
        if (IsAsyncInvocation(invocation) && !IsFusedAwaitedCall(invocation))
            return GenerateFutureCall(invocation, callee, arguments, wrapsErrors);

        if (wrapsErrors)
            return GenerateWrappedCall(callee, arguments);

        var call = new Call(callee, arguments, IsMethodReference(invocation.Expression));
        return _macroExpander.TryGetInvocationMacro(invocation, call, out var replacement) ? replacement : call;
    }

    /// <summary>
    ///     Lowers a call that can raise into an <c>xpcall</c> whose two returns become a
    ///     <c>Result</c>. The handler is a shared runtime function rather than a closure built per
    ///     call site, and the receiver is passed as an argument rather than captured, so nothing is
    ///     allocated on the success path beyond the Result itself.
    /// </summary>
    private IfExpression GenerateWrappedCall(LuauExpression callee, List<LuauExpression> arguments)
    {
        var (ok, value) = GenerateWrappedCallPair(callee, arguments);
        return new IfExpression(ok, OkResult(value), [], ErrorResult(value));
    }

    private static Table OkResult(LuauExpression value) =>
        new([new PropertyTableInitializer("ok", new BooleanLiteral(true)), new PropertyTableInitializer("value", value)]);

    private static Table ErrorResult(LuauExpression error) =>
        new([new PropertyTableInitializer("ok", new BooleanLiteral(false)), new PropertyTableInitializer("error", error)]);

    /// <summary>
    ///     A wrapped call whose Result is propagated straight away never needs the Result to exist:
    ///     the success arm would be built only for <c>.value</c> to be read back out of it on the
    ///     next line. Fusing the two leaves the success path allocating nothing, which matters
    ///     because every fallible Roblox API call reached through '?' would otherwise pay for a table.
    /// </summary>
    private bool TryGeneratePropagatedWrappedCall(Expression expression, [MaybeNullWhen(false)] out LuauExpression value)
    {
        value = null;

        // 'await call()?' is the shape every fallible yielding Roblox member is used in, and the await
        // fuses away into the call, so this fusion has to look straight through it to find the call
        if (expression is Await { Expression: Invocation awaited } && IsFusedAwaitedCall(awaited))
            expression = awaited;

        if (expression is not Invocation invocation || !_semanticModel.TryGetIntrinsicAttribute(invocation.Expression, "wraps_errors", out _))
            return false;

        var callee = Visit(invocation.Expression);
        var arguments = GenerateArguments(invocation);
        var (ok, propagated) = GenerateWrappedCallPair(callee, arguments);
        _state.Prereq(new IfStatement(NegateCondition(ok), new Chunk([new Luau.AST.Return(ErrorResult(propagated))]), [], null));
        value = propagated;

        return true;
    }

    /// <summary>Emits the <c>xpcall</c> and hands back the two locals it returns.</summary>
    private (Luau.AST.Identifier Ok, Luau.AST.Identifier Value) GenerateWrappedCallPair(LuauExpression callee, List<LuauExpression> arguments)
    {
        List<LuauExpression> callArguments = [.. arguments];
        var target = callee;
        if (callee is Luau.AST.PropertyAccess { Target: var instance } access)
        {
            var cached = _state.PushToVariable("_receiver", instance);
            target = new Luau.AST.PropertyAccess(cached, access.Names);
            callArguments.Insert(0, cached);
        }

        var handler = new Luau.AST.PropertyAccess(new Luau.AST.Identifier(LuauFactory.RuntimeImportName), ["roblox_error"]);
        var okName = _state.Scope.AddIdentifier("_ok");
        var valueName = _state.Scope.AddIdentifier("_value");
        _state.Prereq(new MultiConstVariable([okName, valueName], new Call(new Luau.AST.Identifier("xpcall"), [target, handler, .. callArguments])));
        _semanticModel.RuntimeReferences++;

        return (new Luau.AST.Identifier(okName), new Luau.AST.Identifier(valueName));
    }

    public override LuauNode VisitQualifiedName(QualifiedName qualifiedName)
    {
        if (qualifiedName.Names.Exists(n => n.IsOptional))
            return GenerateOptionalChain(
                qualifiedName,
                Visit(qualifiedName.Identifier),
                qualifiedName.Names,
                target => FinalizeOptionalAccess(qualifiedName, qualifiedName.Identifier, qualifiedName.Names, target)
            );

        var luauAccess = new Luau.AST.PropertyAccess(AccessTarget(qualifiedName, qualifiedName.Identifier), qualifiedName.Names.ConvertAll(dotName => dotName.Name.Text));
        if (_macroExpander.TryGetQualifiedNameMacro(qualifiedName, luauAccess, out var propertyReplacement))
            return propertyReplacement;

        return _macroExpander.TryGetInvocationMacroReference(qualifiedName, luauAccess, out var referenceReplacement)
            ? referenceReplacement
            : GenerateRenamedAccess(qualifiedName, luauAccess.Target, luauAccess.Names);
    }

    public override LuauNode VisitPropertyAccess(PropertyAccess propertyAccess)
    {
        // Ahead of the receiver being visited, so 'chain.length' can still count instead of collecting.
        if (_arrayPipeline.TryGenerateLength(propertyAccess, out var measured))
            return measured;

        if (propertyAccess.Names.Exists(n => n.IsOptional))
            return GenerateOptionalChain(
                propertyAccess,
                Visit(propertyAccess.Expression),
                propertyAccess.Names,
                target => FinalizeOptionalAccess(propertyAccess, propertyAccess.Expression, propertyAccess.Names, target)
            );

        var luauAccess = new Luau.AST.PropertyAccess(AccessTarget(propertyAccess, propertyAccess.Expression), propertyAccess.Names.ConvertAll(dotName => dotName.Name.Text));
        if (_macroExpander.TryGetPropertyAccessMacro(propertyAccess, luauAccess, out var propertyReplacement))
            return propertyReplacement;

        return _macroExpander.TryGetInvocationMacroReference(propertyAccess, luauAccess, out var referenceReplacement)
            ? referenceReplacement
            : GenerateRenamedAccess(propertyAccess, luauAccess.Target, luauAccess.Names);
    }

    private LuauExpression FinalizeOptionalAccess(Expression access, Expression rootExpression, List<DotName> names, LuauExpression target)
    {
        if (target is not Luau.AST.PropertyAccess { Names: [.., _] } propertyAccess)
            return target;

        var receiver = propertyAccess.Names.Count > 1
            ? new Luau.AST.PropertyAccess(propertyAccess.Target, propertyAccess.Names[..^1])
            : propertyAccess.Target;

        return _macroExpander.TryGetOptionalChainMemberMacro(access, rootExpression, names, receiver, out var replacement) ? replacement : target;
    }

    private Luau.AST.Identifier GenerateOptionalChain(
        Expression accessExpression,
        LuauExpression target,
        List<DotName> names,
        Func<LuauExpression, LuauExpression>? finalize = null)
    {
        var segments = names.ConvertAll(name => (Name: name.Name.Text, name.IsOptional));
        if (_semanticModel.TryGetIntrinsicAttribute(accessExpression, "luau_name", out var attr) && ValidateLuauNameAttribute(attr, out var nameLiteral))
            segments[^1] = (nameLiteral.Value, segments[^1].IsOptional);

        var resultName = _state.Scope.AddIdentifier("_result");
        var resultIdentifier = new Luau.AST.Identifier(resultName);
        _state.Prereq(new LocalVariable(resultName, null, null));
        _state.Prereq(BuildOptionalChain(target, segments, 0, finalize ?? (expression => expression), resultIdentifier));

        return resultIdentifier;
    }

    private LuauStatement BuildOptionalChain(
        LuauExpression target,
        List<(string Name, bool IsOptional)> segments,
        int index,
        Func<LuauExpression, LuauExpression> finalize,
        Luau.AST.Identifier result)
    {
        if (index >= segments.Count)
            return new ExpressionStatement(new Luau.AST.BinaryOperator(result, "=", finalize(target)));

        if (!segments[index].IsOptional)
        {
            var plainNames = new List<string>();
            var i = index;
            while (i < segments.Count && !segments[i].IsOptional)
            {
                plainNames.Add(segments[i].Name);
                i++;
            }

            return BuildOptionalChain(new Luau.AST.PropertyAccess(target, plainNames), segments, i, finalize, result);
        }

        var cachedTarget = _state.PushToVariable("_target", target);
        var condition = new Luau.AST.BinaryOperator(cachedTarget, "~=", new NilLiteral());
        var nextTarget = new Luau.AST.PropertyAccess(cachedTarget, [segments[index].Name]);

        var (thenStatement, thenScope) = _state.Capture(() => BuildOptionalChain(nextTarget, segments, index + 1, finalize, result));
        var thenStatements = new List<LuauStatement>();
        ApplyPrereqAndPostreq(thenStatements, thenScope, thenStatement);

        return new IfStatement(condition, new Chunk(thenStatements), [], null);
    }

    public override LuauNode VisitElementAccess(ElementAccess elementAccess)
    {
        var target = Visit(elementAccess.Expression);
        var index = Visit(elementAccess.IndexExpression);
        return elementAccess.IsOptional
            ? GenerateOptionalElementAccess(elementAccess, target, index)
            : BuildElementAccess(elementAccess, target, index);
    }

    private LuauExpression BuildElementAccess(ElementAccess elementAccess, LuauExpression target, LuauExpression index)
    {
        var luauAccess = new Luau.AST.ElementAccess(target, index);
        if (_macroExpander.TryGetElementAccessMacro(elementAccess, luauAccess, out var replacement))
            return replacement;

        if (_macroExpander.TryGetInvocationMacroReference(elementAccess, luauAccess, out var referenceReplacement))
            return referenceReplacement;

        return luauAccess.Index is StringLiteral literal
            ? GenerateRenamedAccess(elementAccess, luauAccess.Target, [literal.Value])
            : luauAccess;
    }
    
    private Luau.AST.Identifier GenerateOptionalElementAccess(
        ElementAccess elementAccess,
        LuauExpression target,
        LuauExpression index,
        Func<LuauExpression, LuauExpression>? finalize = null)
    {
        finalize ??= expression => expression;

        var resultName = _state.Scope.AddIdentifier("_result");
        var resultIdentifier = new Luau.AST.Identifier(resultName);
        _state.Prereq(new LocalVariable(resultName, null, null));

        var cachedTarget = _state.PushToVariable("_target", target);
        var condition = new Luau.AST.BinaryOperator(cachedTarget, "~=", new NilLiteral());

        var (thenStatement, thenScope) = _state.Capture(LuauStatement () => new ExpressionStatement(
                new Luau.AST.BinaryOperator(resultIdentifier, "=", finalize(BuildElementAccess(elementAccess, cachedTarget, index)))
            )
        );

        var thenStatements = new List<LuauStatement>();
        ApplyPrereqAndPostreq(thenStatements, thenScope, thenStatement);

        _state.Prereq(new IfStatement(condition, new Chunk(thenStatements), [], null));

        return resultIdentifier;
    }
    
    public override LuauNode VisitErrorPropagation(ErrorPropagation errorPropagation)
    {
        if (TryGeneratePropagatedWrappedCall(errorPropagation.Expression, out var propagated))
            return propagated;

        var target = Visit(errorPropagation.Expression);
        var cached = _state.PushToVariable("_result", target);
        _state.Prereq(
            new IfStatement(
                NegateCondition(new Luau.AST.PropertyAccess(cached, ["ok"])),
                new Chunk([new Luau.AST.Return(cached)]),
                [],
                null
            )
        );

        return new Luau.AST.PropertyAccess(cached, ["value"]);
    }

    public override LuauNode VisitBinaryOperator(BinaryOperator binaryOperator)
    {
        if (SyntaxFacts.IsBitwiseOperator(binaryOperator.Operator.Kind))
            return GenerateBitwiseOperator(binaryOperator);

        if (binaryOperator.Operator.Kind == SyntaxKind.InKeyword)
            return GenerateInOperator(binaryOperator);

        if (binaryOperator.Operator.Kind == SyntaxKind.QuestionQuestion)
            return GenerateNullCoalesce(binaryOperator);

        if (binaryOperator.Operator.Kind is SyntaxKind.AmpersandAmpersandEquals or SyntaxKind.PipePipeEquals or SyntaxKind.QuestionQuestionEquals)
            return GenerateCompoundLogicalAssignment(binaryOperator);

        if (binaryOperator.Operator.Kind == SyntaxKind.AmpersandAmpersand)
            return GenerateLogicalAnd(binaryOperator);

        if (binaryOperator.Operator.Kind == SyntaxKind.PipePipe)
            return GenerateLogicalOr(binaryOperator);

        var op = binaryOperator.Operator.Text;
        var leftType = _semanticModel.GetType(binaryOperator.Left);
        var rightType = _semanticModel.GetType(binaryOperator.Right);
        var @string = PrimitiveType.String;
        var isConcatenation = op.StartsWith('+') && leftType.IsAssignableTo(@string) && rightType.IsAssignableTo(@string);
        var left = Visit(binaryOperator.Left);
        var right = Visit(binaryOperator.Right);
        var mappedOperator = isConcatenation ? op.Replace("+", "..") : LuauOperatorMap.BinaryOperator(op);
        return new Luau.AST.BinaryOperator(left, mappedOperator, right);
    }

    private Luau.AST.BinaryOperator GenerateInOperator(BinaryOperator binaryOperator)
    {
        var index = Visit(binaryOperator.Left);
        var target = Visit(binaryOperator.Right);
        LuauExpression access = index is StringLiteral literal
            ? new Luau.AST.PropertyAccess(target, [literal.Value])
            : new Luau.AST.ElementAccess(target, index);

        return new Luau.AST.BinaryOperator(access, "~=", new NilLiteral());
    }

    // Unlike Luau's `or`, `??` must only substitute on nil, not on falsy `false` - the left side is
    // hoisted to a local since it's referenced in both the nil check and the result.
    private LuauNode GenerateNullCoalesce(BinaryOperator binaryOperator)
    {
        var left = Visit(binaryOperator.Left);
        var (right, rightScope) = _state.Capture(() => Visit(binaryOperator.Right));
        if (HasHoistedStatements(rightScope))
            return GuardRightOperand("_coalesce", left, right, rightScope, result => new Luau.AST.BinaryOperator(result, "==", new NilLiteral()));

        var leftValue = _state.PushToVariable("_coalesce", left);
        var condition = new Luau.AST.BinaryOperator(leftValue, "~=", new NilLiteral());
        return new IfExpression(condition, leftValue, [], right);
    }

    private LuauNode GenerateLogicalAnd(BinaryOperator binaryOperator)
    {
        var left = Visit(binaryOperator.Left);
        var (right, rightScope) = _state.Capture(() => VisitGuardedBy(CollectIsSubstitutions(binaryOperator.Left), binaryOperator.Right));
        return HasHoistedStatements(rightScope)
            ? GuardRightOperand("_and", left, right, rightScope, result => result)
            : new Luau.AST.BinaryOperator(left, "and", right);
    }

    private LuauNode GenerateLogicalOr(BinaryOperator binaryOperator)
    {
        var left = Visit(binaryOperator.Left);
        var (right, rightScope) = _state.Capture(() => Visit(binaryOperator.Right));
        return HasHoistedStatements(rightScope)
            ? GuardRightOperand("_or", left, right, rightScope, NegateCondition)
            : new Luau.AST.BinaryOperator(left, "or", right);
    }

    // Generates the right operand with the bindings an `is` pattern on the left of `&&` introduced in
    // scope, so `x is Some(n) && n > 0` sees `n`.
    private LuauExpression VisitGuardedBy(Dictionary<string, LuauExpression> substitutions, Expression right)
    {
        if (substitutions.Count == 0)
            return Visit(right);

        var previous = _guardSubstitutions;
        var merged = previous == null ? substitutions : new Dictionary<string, LuauExpression>(previous);
        foreach (var (name, value) in substitutions)
            merged[name] = value;

        _guardSubstitutions = merged;
        try
        {
            return Visit(right);
        }
        finally
        {
            _guardSubstitutions = previous;
        }
    }

    // Luau's `and`/`or` short-circuit the expression, but not statements generating one of its operands.
    // A right operand that hoists statements (a macro lowering to a loop, an optional chain, a match)
    // would have them land ahead of the operator and run unconditionally - wasted work when the operand
    // is pure, and the wrong behaviour when it isn't. So the operator promotes itself to a statement
    // form, the same way VisitTernaryOperator does, with the hoisted statements moved under the guard:
    //
    //     local _and = <left>
    //     if _and then
    //         <right's hoisted statements>
    //         _and = <right>
    //     end
    private Luau.AST.Identifier GuardRightOperand(
        string name,
        LuauExpression left,
        LuauExpression right,
        LuauScope rightScope,
        Func<LuauExpression, LuauExpression> guard)
    {
        var resultName = _state.Scope.AddIdentifier(name);
        var resultIdentifier = new Luau.AST.Identifier(resultName);
        _state.Prereq(new LocalVariable(resultName, null, left));

        var statements = new List<LuauStatement>();
        ApplyPrereqAndPostreq(statements, rightScope, new ExpressionStatement(new Luau.AST.BinaryOperator(resultIdentifier, "=", right)));
        _state.Prereq(new IfStatement(guard(resultIdentifier), new Chunk(statements), [], null));

        return resultIdentifier;
    }

    private static bool HasHoistedStatements(LuauScope scope) => scope.PrereqStatements.Count > 0 || scope.PostreqStatements.Count > 0;

    private Dictionary<string, LuauExpression> CollectIsSubstitutions(Expression expression)
    {
        var substitutions = new Dictionary<string, LuauExpression>();
        foreach (var isExpression in TestedBy(expression))
        {
            if (!_isSubjects.TryGetValue(isExpression, out var subject))
                continue;

            foreach (var (name, value) in CollectPatternBindings(isExpression.Pattern, subject))
                substitutions[name] = value;
        }

        return substitutions;
    }

    /// <summary>
    ///     Every <c>is</c> a condition tests, in the order it tests them. Only conjunction and parentheses
    ///     are walked through: what an <c>is</c> binds holds exactly when the whole condition held, which
    ///     <c>&amp;&amp;</c> preserves and <c>||</c> does not.
    /// </summary>
    /// <remarks>
    ///     Both the bindings a branch opens with and the substitutions its body reads are keyed off these,
    ///     and they used to walk the condition's shape separately - so teaching one about a new operator
    ///     left the other quietly disagreeing about which patterns were in scope.
    /// </remarks>
    private static IEnumerable<Is> TestedBy(Expression condition)
    {
        switch (condition)
        {
            case Is isExpression:
                yield return isExpression;
                break;

            case BinaryOperator { Operator.Kind: SyntaxKind.AmpersandAmpersand } and:
                foreach (var nested in TestedBy(and.Left)) yield return nested;
                foreach (var nested in TestedBy(and.Right)) yield return nested;

                break;

            case Parenthesized parenthesized:
                foreach (var nested in TestedBy(parenthesized.Expression)) yield return nested;

                break;
        }
    }

    // Luau has no &&=/||=/??= (unlike +=/-=/etc.), so desugar to a plain `left = left <op> right`. A right
    // operand that hoists statements has to short-circuit the same way the plain operators do, so the
    // desugared value comes from GuardRightOperand's local instead of from an inline `and`/`or`.
    private Luau.AST.BinaryOperator GenerateCompoundLogicalAssignment(BinaryOperator binaryOperator)
    {
        var leftValue = Visit(binaryOperator.Left);
        var (rightValue, rightScope) = _state.Capture(() => Visit(binaryOperator.Right));
        LuauExpression desugaredValue = HasHoistedStatements(rightScope)
            ? binaryOperator.Operator.Kind switch
            {
                SyntaxKind.AmpersandAmpersandEquals => GuardRightOperand("_and", leftValue, rightValue, rightScope, result => result),
                SyntaxKind.PipePipeEquals => GuardRightOperand("_or", leftValue, rightValue, rightScope, NegateCondition),
                _ => GuardRightOperand("_coalesce", leftValue, rightValue, rightScope, result => new Luau.AST.BinaryOperator(result, "==", new NilLiteral()))
            }
            : binaryOperator.Operator.Kind switch
            {
                SyntaxKind.AmpersandAmpersandEquals => new Luau.AST.BinaryOperator(leftValue, "and", rightValue),
                SyntaxKind.PipePipeEquals => new Luau.AST.BinaryOperator(leftValue, "or", rightValue),
                _ => new IfExpression(new Luau.AST.BinaryOperator(leftValue, "~=", new NilLiteral()), leftValue, [], rightValue)
            };

        return new Luau.AST.BinaryOperator(leftValue, "=", desugaredValue);
    }

    public override LuauNode VisitUnaryOperator(UnaryOperator unaryOperator)
    {
        var operand = Visit(unaryOperator.Operand);
        return SyntaxFacts.IsBitwiseOperator(unaryOperator.Operator.Kind)
            ? LuauFactory.Bit32Call("bnot", [operand])
            : new Luau.AST.UnaryOperator(LuauOperatorMap.UnaryOperator(unaryOperator.Operator.Text), operand);
    }

    // A branch that needs to hoist statements (an optional chain, a match, now '?') can't just contribute
    // its value to a plain IfExpression: only the actually-taken branch may run those statements, and
    // Luau's if-expression has nowhere to put a statement at all. So each branch is visited in its own
    // captured scope first; if neither needed to hoist anything, the cheap IfExpression from before is
    // still used. Otherwise the whole ternary becomes a statement-based if/else - mirroring how
    // GenerateOptionalChain/match expressions already promote themselves - assigning to a shared result
    // local so each branch's hoisted statements only run when that branch is actually selected.
    public override LuauNode VisitTernaryOperator(TernaryOperator ternaryOperator)
    {
        var condition = Visit(ternaryOperator.Condition);
        var (thenValue, thenScope) = _state.Capture(() => Visit(ternaryOperator.ThenBranch));
        var (elseValue, elseScope) = _state.Capture(() => Visit(ternaryOperator.ElseBranch));

        if (!HasHoistedStatements(thenScope) && !HasHoistedStatements(elseScope))
            return new IfExpression(condition, thenValue, [], elseValue);

        var resultName = _state.Scope.AddIdentifier("_ternary");
        var resultIdentifier = new Luau.AST.Identifier(resultName);
        _state.Prereq(new LocalVariable(resultName, null, null));

        var thenStatements = new List<LuauStatement>();
        ApplyPrereqAndPostreq(thenStatements, thenScope, new ExpressionStatement(new Luau.AST.BinaryOperator(resultIdentifier, "=", thenValue)));

        var elseStatements = new List<LuauStatement>();
        ApplyPrereqAndPostreq(elseStatements, elseScope, new ExpressionStatement(new Luau.AST.BinaryOperator(resultIdentifier, "=", elseValue)));

        _state.Prereq(new IfStatement(condition, new Chunk(thenStatements), [], new Chunk(elseStatements)));
        return resultIdentifier;
    }

    public override LuauNode VisitParenthesized(Parenthesized parenthesized) => new Luau.AST.Parenthesized(Visit(parenthesized.Expression));

    public override LuauNode VisitRangeLiteral(RangeLiteral rangeLiteral) =>
        new Table([new PropertyTableInitializer("minimum", Visit(rangeLiteral.Minimum)), new PropertyTableInitializer("maximum", Visit(rangeLiteral.Maximum))]);

    public override LuauNode VisitArrayLiteral(ArrayLiteral arrayLiteral) =>
        arrayLiteral.Expressions.Exists(element => element is SpreadElement)
            ? GenerateSpreadArrayLiteral(arrayLiteral.Expressions)
            : new Table(arrayLiteral.Expressions.ConvertAll(e => new TableInitializer(Visit(e))));

    /// <summary>
    ///     Builds the array a statement at a time rather than as one constructor, because a spread's length is
    ///     only known at runtime. A literal that leads with a spread starts from a <c>table.clone</c> of it, so
    ///     a plain copy costs one call; otherwise the elements ahead of the first spread stay in the
    ///     constructor, so the common prepend keeps its literal. Each spread after that is one
    ///     <c>table.move</c> rather than an element-at-a-time loop.
    /// </summary>
    private LuauExpression GenerateSpreadArrayLiteral(List<Expression> elements)
    {
        var leadingCount = elements.FindIndex(element => element is SpreadElement);
        var leadsWithSpread = leadingCount == 0;
        var nextIndex = leadsWithSpread ? 1 : leadingCount;
        LuauExpression initial = leadsWithSpread
            ? LuauFactory.TableCall("clone", [Visit(((SpreadElement)elements[0]).Expression)])
            : new Table(elements.GetRange(0, leadingCount).ConvertAll(e => new TableInitializer(Visit(e))));

        if (nextIndex >= elements.Count)
            return initial;

        var resultName = _state.Scope.AddIdentifier(ArrayLowering.ResultName);
        var result = new Luau.AST.Identifier(resultName);
        _state.Prereq(new ConstVariable(resultName, null, initial));

        LuauExpression initialCount = leadsWithSpread ? new Loom.Luau.AST.UnaryOperator("#", result) : new NumberLiteral(leadingCount);
        var countName = _state.Scope.AddIdentifier(ArrayLowering.CountName);
        var count = new Luau.AST.Identifier(countName);
        _state.Prereq(new LocalVariable(countName, null, initialCount));

        for (var i = nextIndex; i < elements.Count; i++)
        {
            var statements = new List<LuauStatement>();
            if (elements[i] is SpreadElement spreadElement)
            {
                ArrayLowering.AppendSegment(_state, statements, result, count, Visit(spreadElement.Expression));
            }
            else
            {
                var value = Visit(elements[i]);
                statements.Add(new ExpressionStatement(new Luau.AST.BinaryOperator(count, "+=", new NumberLiteral(1))));
                statements.Add(
                    new ExpressionStatement(new Luau.AST.BinaryOperator(new Luau.AST.ElementAccess(result, count), "=", value))
                );
            }

            _state.Prereq([.. statements]);
        }

        return result;
    }

    /// <summary>
    ///     Lowers <paramref name="invocation" />'s arguments, reordering them into declared-parameter order
    ///     first when the call names any of them. Luau itself has no notion of a named argument, but every
    ///     default is already guarded inside the callee's own body (see <see cref="GenerateParameterDefaultGuard" />),
    ///     so a defaulted parameter the caller skipped needs nothing passed for it at the call site beyond
    ///     whatever keeps a later, supplied one in its place - an explicit <c>nil</c> for a gap in the middle,
    ///     nothing at all for one at the end. The type checker has already confirmed every name resolves and
    ///     every required parameter is covered, so this only has to reproduce that ordering, not validate it.
    /// </summary>
    private List<LuauExpression> GenerateArguments(Invocation invocation)
    {
        var argumentList = invocation.Arguments.ArgumentList;
        if (!argumentList.Exists(argument => argument is NamedArgument))
            return GenerateArguments(argumentList);

        var parameters = (_semanticModel.GetSymbol(invocation.Expression)?.Declaration as DeclareFunctionSignature)?.Parameters;
        if (parameters == null)
            return argumentList.ConvertAll(argument => Visit<LuauExpression>(argument is NamedArgument named ? named.Value : argument));

        var fixedParameters = TypeChecking.InvocationArguments.FixedParameters(parameters);
        var slots = new LuauExpression?[fixedParameters.Count];
        var sourceOrder = new List<(int Slot, Expression Value)>();
        var positionalCount = 0;
        foreach (var argument in argumentList)
        {
            if (argument is NamedArgument namedArgument)
            {
                var index = fixedParameters.FindIndex(p => p.Name.Text == namedArgument.Name.Text);
                if (index >= 0)
                    sourceOrder.Add((index, namedArgument.Value));

                continue;
            }

            if (positionalCount < slots.Length)
                sourceOrder.Add((positionalCount, argument));

            positionalCount++;
        }

        var reordered = sourceOrder.Zip(sourceOrder.Skip(1)).Any(pair => pair.Second.Slot < pair.First.Slot);
        foreach (var (slot, value) in sourceOrder)
        {
            var lowered = Visit<LuauExpression>(value);

            // The call evaluates its arguments left to right in declared order, so a call that names them out of
            // order would otherwise run them out of the order they were written in.
            slots[slot] = reordered ? _state.PushIfRepeated(ArrayLowering.ArgumentName, lowered) : lowered;
        }

        var lastSupplied = Array.FindLastIndex(slots, slot => slot != null);
        var result = new List<LuauExpression>(lastSupplied + 1);
        for (var i = 0; i <= lastSupplied; i++)
            result.Add(slots[i] ?? new NilLiteral());

        return result;
    }

    /// <summary>
    ///     Lowers an argument list, expanding a spread with <c>table.unpack</c>.
    /// </summary>
    /// <remarks>
    ///     Only in last position, where Luau expands a call's results into the remaining arguments rather
    ///     than truncating them to one. The type checker has already established that a spread lands in the
    ///     rest parameter, so everything from the first one on belongs to the same array - which is exactly
    ///     what the array-literal lowering builds, and the whole tail goes through it when the spread is not
    ///     alone there.
    /// </remarks>
    private List<LuauExpression> GenerateArguments(List<Expression> argumentList)
    {
        var spreadIndex = argumentList.FindIndex(argument => argument is SpreadElement);
        if (spreadIndex < 0)
            return argumentList.ConvertAll(Visit);

        var arguments = argumentList.GetRange(0, spreadIndex).ConvertAll(Visit);

        // Into names before the tail is built, because the tail lowers to statements that sit above the call
        // while these stay inline in it - so an argument that does something would otherwise do it after the
        // spread had already read what it was about to change. A name or a literal is left where it is.
        for (var i = 0; i < arguments.Count; i++)
            arguments[i] = _state.PushIfRepeated(ArrayLowering.ArgumentName, arguments[i]);

        var tail = argumentList.GetRange(spreadIndex, argumentList.Count - spreadIndex);
        var spread = tail is [SpreadElement only] ? Visit(only.Expression) : GenerateSpreadArrayLiteral(tail);
        arguments.Add(new Spread(spread));

        return arguments;
    }

    public override LuauNode VisitTupleExpression(TupleExpression tupleExpression) =>
        new Table(tupleExpression.Expressions.ConvertAll(e => new TableInitializer(Visit(e))));

    public override LuauNode VisitLiteral(Literal literal) =>
        literal.Value switch
        {
            long l => new NumberLiteral(l),
            int i => new NumberLiteral(i),
            double d => new NumberLiteral(d),
            string s => new StringLiteral(s),
            bool b => new BooleanLiteral(b),
            _ => new NilLiteral()
        };

    public override LuauNode VisitInterpolatedStringLiteral(InterpolatedStringLiteral interpolatedStringLiteral)
    {
        var segments = interpolatedStringLiteral.Parts.ConvertAll<InterpolatedStringSegment>(part => part switch
            {
                InterpolationTextPart text => new InterpolatedStringTextSegment(text.Text),
                InterpolationHolePart hole => new InterpolatedStringExpressionSegment(Visit(hole.Expression)),
                _ => throw new ArgumentException($"Unknown interpolation part type: {part.GetType()}")
            }
        );

        return new InterpolatedString(segments);
    }

    public override LuauNode VisitIdentifier(Identifier identifier)
    {
        if (_guardSubstitutions != null && _guardSubstitutions.TryGetValue(identifier.Name.Text, out var substitution))
            return substitution;

        var symbol = _semanticModel.GetSymbol(identifier);
        if (symbol != null
            && symbol.TryGetIntrinsicAttribute("luau_name", out var luauNameAttribute)
            && ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral))
            return new Luau.AST.Identifier(nameLiteral.Value);

        var luauIdentifier = new Luau.AST.Identifier(identifier.Name.Text);
        if (_macroExpander.TryGetInvocationMacroReference(identifier, luauIdentifier, out var referenceReplacement))
            return referenceReplacement;

        return symbol is { Kind: SymbolKind.InjectedPropertyVariable } || IsImplementMethodSymbol(symbol)
            ? new Luau.AST.PropertyAccess(LuauFactory.Self, [luauIdentifier.Name])
            : luauIdentifier;
    }

    // A bare call to a sibling (or its own) method inside an `implement` block resolves to an ordinary
    // SymbolKind.Function - correctly scoped to that block by the resolver - but still needs routing
    // through 'self' and Luau's ':' call syntax, the same as an explicit `@.method()` would.
    private static bool IsImplementMethodSymbol(Symbol? symbol) =>
        symbol is { Kind: SymbolKind.Function, Declaration: FunctionDeclaration { Parent: ImplementBody } };

    public override LuauNode VisitSelfExpression(SelfExpression selfExpression) => LuauFactory.Self;

    public override LuauNode VisitAs(As @as) => new TypeCast(Visit(@as.Expression), Visit(@as.Type));

    public override LuauNode VisitNullForgiving(NullForgiving nullForgiving)
    {
        _semanticModel.RuntimeReferences += 1;
        var expression = Visit(nullForgiving.Expression);
        return new TypeCast(expression, LuauFactory.QualifyRuntimeType(new Luau.AST.TypeName("NonNullable", [new TypeOfType(expression)])));
    }

    public override LuauNode VisitNameOf(NameOf nameOf) => new StringLiteral(((LiteralType)_semanticModel.GetType(nameOf)).Value!.ToString()!);

    public override LuauNode VisitIs(Is @is)
    {
        var subject = Visit(@is.Expression);
        _isSubjects[@is] = subject;

        var conditions = new List<LuauExpression>();
        var bindings = new List<LuauStatement>();
        TryCompilePattern(@is.Pattern, subject, conditions, bindings, out _);

        var prelude = new List<LuauStatement>();
        if (@is is { Expression: AssignmentTarget, Pattern: TypePattern typePattern })
            prelude.Add(new ExpressionStatement(new Luau.AST.BinaryOperator(subject, "=", new TypeCast(subject, Visit(typePattern.Type)))));

        prelude.AddRange(bindings);
        _isPreludes[@is] = prelude;

        return CombineWith(conditions, "and");
    }

    public override LuauNode VisitInterfaceInvocation(InterfaceInvocation interfaceInvocation)
    {
        var symbol = _semanticModel.GetSymbol(interfaceInvocation.Name, SymbolKind.Interface);

        // the resolver already reported why this is not an interface; an invocation sits in expression
        // position, so a statement placeholder here would fail to cast and surface as a compiler crash
        if (symbol is not InterfaceSymbol interfaceSymbol)
            return new NilLiteral();

        var table = GenerateInterfaceInvocationBody(interfaceInvocation.Body, interfaceSymbol);
        return WrapWithImplementationMetatable(table, interfaceSymbol, interfaceInvocation.Name.Token.Text, interfaceInvocation);
    }

    /// <summary>
    ///     A table built for an interface that implements one or more traits needs the trait's methods
    ///     reachable through <c>__index</c>, since they live on a shared metatable rather than as own
    ///     fields of any one instance - shared by every site that produces a fresh instance of the
    ///     interface (<c>new X { ... }</c> and the 'with' operator alike).
    /// </summary>
    private LuauExpression WrapWithImplementationMetatable(Table table, InterfaceSymbol interfaceSymbol, string typeName, Node site)
    {
        if (interfaceSymbol.Implements.Count == 0)
            return table;

        // Loom hoists an interface's 'implement' blocks for type-checking - a construction site may
        // legally precede all of them in source - but Luau does not hoist the locals they emit: a
        // trait's table has to be declared before this setmetatable call references it, or the
        // reference resolves to an undeclared global 'nil' and that trait's methods silently vanish
        // (see LuauGeneratorTest's ordering regression test for the exact failure this used to produce).
        if (interfaceSymbol.Implementations.Find(i => !_generatedImplements.Contains(i)) is { } notYetGenerated)
        {
            var traitName = _semanticModel.GetSymbol(notYetGenerated.TraitName, SymbolKind.Trait)?.Name ?? notYetGenerated.TraitName.ToString();
            _diagnostics.Error(
                site,
                InternalCodes.ConstructedBeforeImplement,
                $"'{typeName}' is constructed here before 'implement {traitName} for {typeName}', which appears later in the file.",
                $"move 'implement {traitName} for {typeName}' above this point."
            );
        }

        var meta = interfaceSymbol.Implementations.Count == 1
            ? new Luau.AST.Identifier(GetImplementationMetaName(interfaceSymbol.Implementations[0]))
            : GetMergedMetatable(interfaceSymbol);

        var call = LuauFactory.SetMetatableCall(table, meta);
        return new TypeCast(call, new Luau.AST.TypeName(typeName));
    }

    /// <summary>
    ///     Reuses the merged metatable <see cref="EmitMergedMetatableOnceComplete" /> already built as a
    ///     top-level local, shared by every construction site of a multi-trait interface instead of each
    ///     calling <c>Loom.merge_meta</c> (a fresh closure and table) for itself. The pre-pass behind <see
    ///     cref="_multiTraitInterfacesConstructed" /> guarantees this exists by the time any LEGALLY placed
    ///     construction site is reached; the only way to miss it is the ConstructedBeforeImplement case just
    ///     diagnosed above, where the interface's own trait tables are not fully declared yet either - so
    ///     falling back to an inline (unshared) merge there keeps the tree well-formed without pretending
    ///     the already-reported error away.
    /// </summary>
    private LuauExpression GetMergedMetatable(InterfaceSymbol interfaceSymbol)
    {
        if (_mergedMetatables.TryGetValue(interfaceSymbol, out var existing))
            return existing;

        var metaNames = interfaceSymbol.Implementations.ConvertAll(LuauExpression (i) => new Luau.AST.Identifier(GetImplementationMetaName(i)));
        _semanticModel.RuntimeReferences += 1;
        return LuauFactory.RuntimeLibraryCall(["merge_meta"], metaNames);
    }

    private Table GenerateInterfaceInvocationBody(InterfaceInvocationBody interfaceInvocationBody, InterfaceSymbol interfaceSymbol)
    {
        var unhandledInitializer = interfaceInvocationBody.Initializers
            .Find(i => i is not (IndexInitializer
                              or PropertyInitializer
                              or ShorthandPropertyInitializer)
            );

        if (unhandledInitializer != null)
            _diagnostics.CompilerError(unhandledInitializer, "Unhandled interface invocation initializer type");

        return new Table(
            interfaceInvocationBody.Initializers.ConvertAll(TableInitializer (i) => i switch
                {
                    IndexInitializer indexInitializer => GenerateInterfaceInvocationIndexInitializer(indexInitializer),
                    PropertyInitializer propertyInitializer => GenerateInterfaceInvocationPropertyInitializer(
                        propertyInitializer,
                        interfaceSymbol
                    ),
                    ShorthandPropertyInitializer shorthandPropertyInitializer => GenerateInterfaceInvocationShorthandPropertyInitializer(
                        shorthandPropertyInitializer,
                        interfaceSymbol
                    ),
                    _ => null!
                }
            )
        );
    }

    private ComputedPropertyTableInitializer GenerateInterfaceInvocationIndexInitializer(IndexInitializer indexInitializer) =>
        new(Visit(indexInitializer.IndexExpression), Visit(indexInitializer.Expression));

    private PropertyTableInitializer GenerateInterfaceInvocationPropertyInitializer(
        PropertyInitializer propertyInitializer,
        InterfaceSymbol interfaceSymbol)
    {
        var name = propertyInitializer.Name.Text;
        var renamedName = GetRenamedPropertyName(interfaceSymbol, name);
        return new PropertyTableInitializer(renamedName, Visit(propertyInitializer.Expression));
    }

    private string GetRenamedPropertyName(InterfaceSymbol interfaceSymbol, string name)
    {
        var propertySymbol = interfaceSymbol.GetPropertyAtPath([name]);
        return propertySymbol == null
            || !propertySymbol.TryGetIntrinsicAttribute("luau_name", out var luauNameAttribute)
            || !ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral)
                ? name
                : nameLiteral.Value;
    }

    private PropertyTableInitializer GenerateInterfaceInvocationShorthandPropertyInitializer(
        ShorthandPropertyInitializer shorthandPropertyInitializer,
        InterfaceSymbol interfaceSymbol)
    {
        var name = shorthandPropertyInitializer.Identifier.Name.Text;
        var renamedName = GetRenamedPropertyName(interfaceSymbol, name);
        return new PropertyTableInitializer(renamedName, Visit(shorthandPropertyInitializer.Identifier));
    }

    /// <summary>
    ///     Every field of the left operand's interface becomes a table entry: one the '{ ... }' block lists
    ///     is generated the same way a <c>new X { ... }</c> initializer would be, and one it leaves out reads
    ///     the left operand's own value for that field - inlined field-by-field at compile time, never a
    ///     runtime merge.
    /// </summary>
    public override LuauNode VisitWithOperator(WithOperator withOperator)
    {
        // the type checker already reported why this isn't an interface; 'with' sits in expression
        // position, so a statement placeholder here would fail to cast and surface as a compiler crash
        if (_semanticModel.GetType(withOperator.Expression) is not InterfaceType interfaceType)
            return new NilLiteral();

        var left = _state.PushIfRepeated("with_subject", Visit(withOperator.Expression));
        if (_semanticModel.FindTypeDeclaration(interfaceType.Name) is not InterfaceSymbol interfaceSymbol)
        {
            _diagnostics.CompilerError(withOperator, "'with' operand's interface has no declaration symbol");
            return new NilLiteral();
        }

        var overridden = new Dictionary<string, PropertyTableInitializer>();
        var indexOverrides = new List<TableInitializer>();
        foreach (var initializer in withOperator.Body.Initializers)
            switch (initializer)
            {
                case PropertyInitializer propertyInitializer:
                    overridden[propertyInitializer.Name.Text] = GenerateInterfaceInvocationPropertyInitializer(propertyInitializer, interfaceSymbol);
                    break;
                case ShorthandPropertyInitializer shorthandPropertyInitializer:
                    overridden[shorthandPropertyInitializer.Identifier.Name.Text] =
                        GenerateInterfaceInvocationShorthandPropertyInitializer(shorthandPropertyInitializer, interfaceSymbol);
                    break;
                case IndexInitializer indexInitializer:
                    indexOverrides.Add(GenerateInterfaceInvocationIndexInitializer(indexInitializer));
                    break;
            }

        // trait methods live on the metatable, not as own fields, so a value's own type carrying them
        // (only true once it has gone through 'new X { ... }') must not turn them into copied fields here
        var fields = interfaceType.Properties
            .Where(property => !interfaceType.TraitMethodNames.Contains(property.Name))
            .Select(TableInitializer (property) =>
            {
                if (overridden.TryGetValue(property.Name, out var initializer))
                    return initializer;

                var renamedName = GetRenamedPropertyName(interfaceSymbol, property.Name);
                return new PropertyTableInitializer(renamedName, new Luau.AST.PropertyAccess(left, [renamedName]));
            })
            .Concat(indexOverrides)
            .ToList();

        return WrapWithImplementationMetatable(new Table(fields), interfaceSymbol, interfaceType.Name, withOperator);
    }

    /// <summary>
    ///     Renames every step of the chain that needs it, not just the last - <c>a.b.c</c> can have a
    ///     <c>luau_name</c> at each step (e.g. <c>frame.absolute_position.x</c> needs both
    ///     'absolute_position' → 'AbsolutePosition' and 'x' → 'X'), and a step with no attribute keeps its
    ///     own name unchanged.
    /// </summary>
    private Luau.AST.PropertyAccess GenerateRenamedAccess(Expression access, LuauExpression target, List<string> names)
    {
        var symbolChain = _semanticModel.GetPropertySymbolChain(access);
        var renamed = names.Select((name, index) =>
            index < symbolChain.Count
            && symbolChain[index] is { } symbol
            && symbol.TryGetIntrinsicAttribute("luau_name", out var attr)
            && ValidateLuauNameAttribute(attr, out var nameLiteral)
                ? nameLiteral.Value
                : name
        ).ToList();

        return new Luau.AST.PropertyAccess(target, renamed);
    }

    /// <summary>
    ///     Determines whether the given expression refers to a trait method that must be called
    ///     with Luau's ':' method-call syntax, as opposed to a plain function value.
    /// </summary>
    private bool IsMethodReference(Expression expression) =>
        _semanticModel.TryGetIntrinsicAttribute(expression, "luau_method", out _)
        || expression is Identifier identifier && IsImplementMethodSymbol(_semanticModel.GetSymbol(identifier))
        || expression is { Children: [{ } child, ..], Tokens: [.., { Kind: SyntaxKind.Identifier } name] }
        && _semanticModel.GetType(child) is InterfaceType interfaceType
        && interfaceType.TraitMethodNames.Contains(name.Text);

    private LuauExpression WrapAnonymousFunction(Expression expression, LuauExpression luauExpression, LuauType? returnType = null)
    {
        if (luauExpression is Luau.AST.Identifier || !IsMethodReference(expression))
            return luauExpression;

        return new AnonymousFunction(
            null,
            [Parameter.Vararg()],
            returnType,
            new Chunk([new ExpressionStatement(new Call(luauExpression, [new Luau.AST.Identifier("...")], true))])
        );
    }

    private List<LuauExpression> UnwrapFunctionArgument(Statement body, LuauType? returnType = null)
    {
        var chunk = _state.CaptureIsolated(() => GenerateChunk(body));
        return chunk is { Statements: [ExpressionStatement { Expression: Call { IsMethod: false } call }] }
            ? [call.Callee, ..call.Arguments]
            : [new AnonymousFunction(null, [], returnType ?? new UnitType(), chunk)];
    }

    private LuauNode GenerateBitwiseOperator(BinaryOperator binaryOperator)
    {
        var left = Visit(binaryOperator.Left);
        var right = Visit(binaryOperator.Right);
        var op = binaryOperator.Operator.Text;
        if (op.EndsWith('='))
            return GenerateBitwiseAssignmentOperator(op, left, right);

        var name = LuauOperatorMap.BitwiseOperator(op);
        var arguments = new List<LuauExpression>();
        var leftUpdated = AddBit32Arguments(left, name, arguments);
        var rightUpdated = AddBit32Arguments(right, name, arguments);
        if (!leftUpdated)
            arguments.Add(left);

        if (!rightUpdated)
            arguments.Add(right);

        return LuauFactory.Bit32Call(name, arguments);
    }

    private static Luau.AST.BinaryOperator GenerateBitwiseAssignmentOperator(string op, LuauExpression left, LuauExpression right)
    {
        var name = LuauOperatorMap.BitwiseOperator(op);
        var arguments = new List<LuauExpression> { left };
        var rightUpdated = AddBit32Arguments(right, name, arguments);
        if (!rightUpdated)
            arguments.Add(right);

        return new Luau.AST.BinaryOperator(left, "=", LuauFactory.Bit32Call(name, arguments));
    }

    private static bool AddBit32Arguments(LuauExpression expression, string name, List<LuauExpression> arguments)
    {
        if (expression is not Call
            {
                Callee: Luau.AST.PropertyAccess { Target: Luau.AST.Identifier { Name: "bit32" }, Names: [{ } fnName] }, Arguments: { } fnArguments
            }
            || fnName != name)
            return false;

        // shift methods dont accept varargs
        if (fnName.EndsWith("shift"))
            return false;

        foreach (var argument in fnArguments)
        {
            arguments.Add(argument);
            AddBit32Arguments(argument, name, arguments);
        }

        return true;
    }
}