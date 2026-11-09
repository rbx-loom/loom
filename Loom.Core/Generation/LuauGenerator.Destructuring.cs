using Loom.Core.Parsing.AST;
using Loom.Luau;
using Loom.Luau.AST;
using TupleType = Loom.Core.TypeChecking.Types.TupleType;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitDestructuringDeclaration(DestructuringDeclaration destructuringDeclaration)
    {
        if (destructuringDeclaration.EqualsValueClause == null)
            return new NoOpStatement();

        var initializerExpression = destructuringDeclaration.EqualsValueClause.Value;
        if (destructuringDeclaration.Target is TupleDestructuringTarget tupleTarget)
            return EmitTupleDestructuring(tupleTarget, initializerExpression);

        var initializer = Visit(initializerExpression);
        var subject = _state.PushToVariable("_destructure", initializer);
        EmitDestructuringTarget(destructuringDeclaration.Target, subject);

        return new NoOpStatement();
    }

    /// <summary>
    ///     Emits every leaf binding under <paramref name="target" />, reading each one off <paramref name="subject" />
    ///     - the value this target destructures. A field or element that renames into a nested pattern instead
    ///     of a plain name contributes no local of its own here: <see cref="Luau.AST.PropertyAccess" />/<see cref="Luau.AST.ElementAccess" />
    ///     already nest (<c>user.address.city</c> is just a <c>PropertyAccess</c> whose own target is another
    ///     <c>PropertyAccess</c>), so recursing with the deeper access expression as the new subject is enough to
    ///     flatten arbitrarily nested patterns into one local per leaf, exactly as a top-level one already was.
    /// </summary>
    private void EmitDestructuringTarget(DestructuringTarget target, LuauExpression subject)
    {
        switch (target)
        {
            case ArrayDestructuringTarget arrayTarget:
                for (var i = 0; i < arrayTarget.Elements.Count; i++)
                    EmitDestructuringElement(arrayTarget.Elements[i], new Luau.AST.ElementAccess(subject, new NumberLiteral(i + 1)));

                break;

            case ObjectDestructuringTarget objectTarget:
                foreach (var field in objectTarget.Fields)
                    EmitObjectDestructuringField(field, new Luau.AST.PropertyAccess(subject, [field.Name.Text]));

                break;

            case TupleDestructuringTarget tupleTarget:
                var names = tupleTarget.Elements.ConvertAll(e => e.Name?.Text ?? "_");
                foreach (var name in names)
                    _state.Scope.AddIdentifier(name);

                _state.Prereq(new MultiConstVariable(names, LuauFactory.TableCall("unpack", [subject])));
                break;
        }
    }

    private void EmitDestructuringElement(DestructuringElement element, LuauExpression access)
    {
        if (element.NestedTarget != null)
        {
            EmitDestructuringTarget(element.NestedTarget, EmitDefaultedSubject(access, element.EqualsValueClause));
            return;
        }

        EmitDestructuringBinding(element.Name!.Text, access, element.EqualsValueClause);
    }

    private void EmitObjectDestructuringField(ObjectDestructuringField field, LuauExpression access)
    {
        if (field.NestedTarget != null)
        {
            EmitDestructuringTarget(field.NestedTarget, EmitDefaultedSubject(access, field.EqualsValueClause));
            return;
        }

        EmitDestructuringBinding(field.BindingName.Text, access, field.EqualsValueClause);
    }

    /// <summary>
    ///     Emits the local a leaf binding reads off <paramref name="access" />. A binding with no default stays
    ///     <c>const</c>, exactly as before defaults existed; one with a default needs a plain <c>local</c> instead,
    ///     since the guard right after it reassigns the value when <paramref name="access" /> turned out nil.
    /// </summary>
    private void EmitDestructuringBinding(string name, LuauExpression access, EqualsValueClause? equalsValueClause)
    {
        _state.Scope.AddIdentifier(name);
        if (equalsValueClause == null)
        {
            _state.Prereq(new ConstVariable(name, null, access));
            return;
        }

        _state.Prereq(new LocalVariable(name, null, access), GenerateDefaultGuard(name, equalsValueClause));
    }

    /// <summary>
    ///     Applies a default to the subject a nested target destructures, when that position has one -
    ///     <c>[[a, b] = [1, 2]]</c> falls back to <c>[1, 2]</c> before <c>a</c>/<c>b</c> read off it. With no
    ///     default the access expression is threaded straight through, preserving the property-chain-only
    ///     emission <see cref="EmitDestructuringTarget" /> relies on for a plain nested pattern.
    /// </summary>
    private LuauExpression EmitDefaultedSubject(LuauExpression access, EqualsValueClause? equalsValueClause)
    {
        if (equalsValueClause == null)
            return access;

        var subject = _state.PushToVariable("_destructure", access, isConst: false);
        _state.Prereq(GenerateDefaultGuard(subject.Name, equalsValueClause));
        return subject;
    }

    /// <summary>
    ///     Tuple destructuring is kept out of the general array/object path above because it never needs a
    ///     table at all when the right-hand side already has its values in hand: a literal tuple binds each
    ///     name straight to its corresponding sub-expression, and a tuple-returning call already yields genuine
    ///     Luau multi-return values (whether that callee used the literal-return path or `table.unpack`
    ///     internally). Only a plain tuple-typed value (already living in a table) needs `table.unpack`.
    /// </summary>
    private NoOpStatement EmitTupleDestructuring(TupleDestructuringTarget target, Expression initializerExpression)
    {
        var names = target.Elements.ConvertAll(e => e.Name?.Text ?? "_");
        foreach (var name in names)
            _state.Scope.AddIdentifier(name);

        if (initializerExpression is TupleExpression tupleExpression)
        {
            for (var i = 0; i < names.Count; i++)
                _state.Prereq(new ConstVariable(names[i], null, Visit(tupleExpression.Expressions[i])));

            return new NoOpStatement();
        }

        var initializer = Visit(initializerExpression);
        if (initializerExpression is Invocation && _semanticModel.GetType(initializerExpression) is TupleType)
        {
            _state.Prereq(new MultiConstVariable(names, initializer));
            return new NoOpStatement();
        }

        var subject = _state.PushToVariable("_destructure", initializer);
        _state.Prereq(new MultiConstVariable(names, LuauFactory.TableCall("unpack", [subject])));
        return new NoOpStatement();
    }
}
