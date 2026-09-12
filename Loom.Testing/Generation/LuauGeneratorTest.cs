using Loom.Core.Diagnostics;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using ElementAccess = Loom.Luau.AST.ElementAccess;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using FunctionType = Loom.Luau.AST.FunctionType;
using Identifier = Loom.Luau.AST.Identifier;
using IntersectionType = Loom.Luau.AST.IntersectionType;
using OptionalType = Loom.Luau.AST.OptionalType;
using Parenthesized = Loom.Luau.AST.Parenthesized;
using ParenthesizedType = Loom.Luau.AST.ParenthesizedType;
using PrimitiveType = Loom.Luau.AST.PrimitiveType;
using PropertyAccess = Loom.Luau.AST.PropertyAccess;
using Return = Loom.Luau.AST.Return;
using TypeAlias = Loom.Luau.AST.TypeAlias;
using TypeName = Loom.Luau.AST.TypeName;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;
using UnionType = Loom.Luau.AST.UnionType;

namespace Loom.Testing.Generation;

[Collection("Assembly")]
public class LuauGeneratorTest
{
    [Theory]
    [InlineData("declare let x: number;")]
    [InlineData("declare mut x: number;")]
    [InlineData("declare fn x(): number;")]
    [InlineData("declare event x(param: number);")]
    [InlineData("import { square } from \"./math\"")]
    [InlineData("import { square as sq } from \"./math\"")]
    [InlineData("import type { Vector } from \"./vector\"")]
    public void Generates_Nothing(string source) => Assert.Empty(Utility.GetLuauAST(source).Statements);

    /// <remarks>
    ///     A node the parser or resolver could not make sense of generates something of the wrong kind. The
    ///     error is already reported and the output is never written, so the generator stands in for the node
    ///     and carries on rather than taking the whole file down with it.
    /// </remarks>
    [Theory]
    [InlineData("let x = ;", "const x = nil")]
    [InlineData("let v: Missing = 1;", "const v: unknown = 1")]
    [InlineData("let x", "const x = nil")]
    public void Generates_APlaceholder_ForANodeItCannotGenerate(string source, string expected) =>
        Assert.Equal(expected, Utility.GetLuauAST(source).Render().Trim());

    /// <summary>
    ///     'internal' only narrows who may <em>import</em> a name - the module itself still returns it the
    ///     same way an ordinary export would, since another file in the same root still reaches it through
    ///     require().
    /// </summary>
    [Fact]
    public void Generates_AnInternalMember_IntoTheModuleTable_LikeAnOrdinaryExport()
    {
        const string source = """
            internal fn hash_key(k: number): number -> k;
            export fn get(k: number): number -> hash_key(k);
            """;

        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("return { hash_key = hash_key, get = get }", rendered);
    }

    [Theory]
    [InlineData("export type Alias = number;")]
    [InlineData("export interface Point { x: number }")]
    [InlineData("export enum Direction { Up, Down }")]
    [InlineData("export trait Drawable { fn draw: void; }")]
    public void Generates_ExportedTypeAlias_WithoutExportTable(string source)
    {
        var statements = Utility.GetLuauAST(source, true).Statements;
        var typeAlias = Assert.IsType<TypeAlias>(Assert.Single(statements));
        Assert.True(typeAlias.IsExported);
    }

    [Fact]
    public void Generates_ExportTable_WithValueExportsOnly()
    {
        var statements = Utility.GetLuauAST("export let x = 1; export type A = number;", true).Statements;
        var table = Assert.IsType<Table>(Assert.IsType<Return>(statements.Last()).Expression);
        var initializer = Assert.IsType<PropertyTableInitializer>(Assert.Single(table.Initializers));

        Assert.Equal("x", initializer.PropertyName);
        Assert.Equal("x", Assert.IsType<Identifier>(initializer.Value).Name);
    }

    [Theory]
    [InlineData("##hello!")]
    [InlineData("#:hello!:#")]
    public void Generates_Comments(string source)
    {
        var luauTree = Utility.GetLuauAST(source);
        Assert.Single(luauTree.Statements);

        var comment = Assert.IsType<Comment>(luauTree.Statements.First());
        Assert.Equal("hello!", comment.Content);
    }

    [Fact]
    public void Generates_NestedKeyOfType()
    {
        var luauTree = Utility.GetLuauAST("type Abc = number; mut x: keyof(keyof(Abc));");
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.Last());
        Assert.NotNull(variable.DeclaredType);

        var outerKeyOf = Assert.IsType<TypeName>(variable.DeclaredType);
        Assert.Equal("keyof", outerKeyOf.Name);
        Assert.Single(outerKeyOf.TypeArguments);

        var innerKeyOf = Assert.IsType<TypeName>(outerKeyOf.TypeArguments.First());
        Assert.Equal("keyof", innerKeyOf.Name);
        Assert.Single(innerKeyOf.TypeArguments);

        var arg = Assert.IsType<TypeName>(innerKeyOf.TypeArguments.First());
        Assert.Equal("Abc", arg.Name);
        Assert.Empty(arg.TypeArguments);
    }

    [Fact]
    public void Generates_KeyOfInTypeAlias()
    {
        var luauTree = Utility.GetLuauAST("type I = number; type Keys = keyof(I);");
        Assert.Equal(2, luauTree.Statements.Count);

        var alias = Assert.IsType<TypeAlias>(luauTree.Statements.Last());
        var keyOfType = Assert.IsType<TypeName>(alias.Type);
        Assert.Equal("keyof", keyOfType.Name);
        Assert.Single(keyOfType.TypeArguments);
        var arg = Assert.IsType<TypeName>(keyOfType.TypeArguments.First());
        Assert.Equal("I", arg.Name);
        Assert.Empty(arg.TypeArguments);
    }

    [Fact]
    public void Generates_KeyOfOnGenericInstantiation()
    {
        var luauTree = Utility.GetLuauAST("interface I<T> { value: T } type Keys = keyof(I<number>);");
        Assert.Equal(2, luauTree.Statements.Count);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Last());
        var keyOfType = Assert.IsType<TypeName>(typeAlias.Type);
        Assert.Equal("keyof", keyOfType.Name);
        Assert.Single(keyOfType.TypeArguments);

        var genericType = Assert.IsType<TypeName>(keyOfType.TypeArguments.First());
        Assert.Equal("I", genericType.Name);
        Assert.Single(genericType.TypeArguments);
        var arg = Assert.IsType<PrimitiveType>(genericType.TypeArguments.First());
        Assert.Equal(PrimitiveTypeKind.Number, arg.Kind);
    }

    [Fact]
    public void Generates_TypeOfInTypeAlias()
    {
        var luauTree = Utility.GetLuauAST("let x = 5; type X = typeof(x);");
        Assert.Equal(2, luauTree.Statements.Count);

        var alias = Assert.IsType<TypeAlias>(luauTree.Statements.Last());
        var typeOf = Assert.IsType<TypeOfType>(alias.Type);
        var identifier = Assert.IsType<Identifier>(typeOf.Expression);
        Assert.Equal("x", identifier.Name);
    }

    [Fact]
    public void Generates_TypeOfOnPropertyAccess()
    {
        var luauTree = Utility.GetLuauAST("interface I { a: number } let i = new I { a: 1 }; type X = typeof(i.a);");
        var alias = Assert.IsType<TypeAlias>(luauTree.Statements.Last());
        var typeOf = Assert.IsType<TypeOfType>(alias.Type);
        Assert.IsType<PropertyAccess>(typeOf.Expression);
        Assert.Equal("typeof(i.a)", typeOf.Render(new RenderState()));
    }

    [Fact]
    public void Generates_TypeOfInVariableType()
    {
        var luauTree = Utility.GetLuauAST("let a = 5; let x: typeof(a) = a;");
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        Assert.NotNull(variable.DeclaredType);
        var typeOf = Assert.IsType<TypeOfType>(variable.DeclaredType);
        var identifier = Assert.IsType<Identifier>(typeOf.Expression);
        Assert.Equal("a", identifier.Name);
    }

    [Fact]
    public void Generates_KeyOfWithIndexedAccess()
    {
        var luauTree = Utility.GetLuauAST("type I = number; type Keys = keyof(I['prop']);");
        Assert.Equal(2, luauTree.Statements.Count);

        var alias = Assert.IsType<TypeAlias>(luauTree.Statements.Last());
        var keyOf = Assert.IsType<TypeName>(alias.Type);
        Assert.Equal("keyof", keyOf.Name);
        Assert.Single(keyOf.TypeArguments);

        var indexed = Assert.IsType<TypeName>(keyOf.TypeArguments.First());
        Assert.Equal("index", indexed.Name);
        Assert.Equal(2, indexed.TypeArguments.Count);
        var target = Assert.IsType<TypeName>(indexed.TypeArguments.First());
        Assert.Equal("I", target.Name);
        var index = Assert.IsType<StringLiteralType>(indexed.TypeArguments.Last());
        Assert.Equal("prop", index.Value);
    }

    [Fact]
    public void Generates_NamedArgument_SkippingMiddleDefault_PassesNilForTheGap()
    {
        const string source = """
            fn move_to(target: number, speed: number = 16, smooth: bool = false): void { }
            move_to(target: 1, smooth: true);
            """;

        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("move_to(1, nil, true)", rendered);
    }

    [Fact]
    public void Generates_NamedArgument_MixedWithPositional_ReordersToDeclaredPosition()
    {
        const string source = """
            fn move_to(target: number, speed: number = 16, smooth: bool = false): void { }
            move_to(1, smooth: true);
            """;

        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("move_to(1, nil, true)", rendered);
    }

    [Fact]
    public void Generates_NamedArgument_OutOfOrder_ReordersToDeclaredPosition()
    {
        const string source = """
            fn move_to(target: number, speed: number = 16, smooth: bool = false): void { }
            move_to(smooth: true, speed: 4, target: 1);
            """;

        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("move_to(1, 4, true)", rendered);
    }

    [Fact]
    public void Generates_NamedArgument_TrailingOmission_DropsItEntirely()
    {
        const string source = """
            fn move_to(target: number, speed: number = 16): void { }
            move_to(target: 1);
            """;

        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("move_to(1)", rendered);
        Assert.DoesNotContain("nil", rendered.Split('\n')[^1]);
    }

    [Fact]
    public void Generates_TernaryOp()
    {
        var luauTree = Utility.GetLuauAST("true ? 69 : 'abc'");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var ifExpression = Assert.IsType<IfExpression>(variable.Initializer);
        var condition = Assert.IsType<BooleanLiteral>(ifExpression.Condition);
        Assert.True(condition.Value);

        var number = Assert.IsType<NumberLiteral>(ifExpression.ThenBranch);
        Assert.Equal(69, number.Value);

        var @string = Assert.IsType<StringLiteral>(ifExpression.ElseBranch);
        Assert.Equal("abc", @string.Value);
    }

    [Fact]
    public void Generates_TernaryOperator_WithHoistingBranch_PromotesToStatementForm()
    {
        // Neither branch may run unless its own side of the condition is actually taken - a plain
        // IfExpression can't guarantee that, since Visit()-ing both branches unconditionally hoists
        // whatever they need (e.g. an error-propagation guard) into the same shared, unconditional scope.
        var luauTree = Utility.GetLuauAST(
            """
            fn a(): Result<number, string> { return Result::ok(1); }
            fn b(): Result<number, string> { return Result::ok(2); }
            fn pick(cond: bool): Result<number, string> {
                let value = cond ? a()? : b()?;
                return Result::ok(value);
            }
            """,
            true
        );

        var pick = luauTree.Statements.OfType<Function>().Single(f => f.Name == "pick");
        var ternaryLocal = Assert.IsType<LocalVariable>(pick.Body.Statements[0]);
        Assert.Equal("_ternary", ternaryLocal.Name);

        var ifStatement = Assert.IsType<IfStatement>(pick.Body.Statements[1]);
        Assert.Equal("cond", Assert.IsType<Identifier>(ifStatement.Condition).Name);

        var thenGuard = Assert.IsType<IfStatement>(ifStatement.ThenBranch.Statements[1]);
        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[2]).Expression);
        Assert.Equal("_ternary", Assert.IsType<Identifier>(thenAssignment.Left).Name);
        Assert.NotNull(thenGuard.ThenBranch.Statements.OfType<Return>().SingleOrDefault());

        var elseGuard = Assert.IsType<IfStatement>(ifStatement.ElseBranch!.Statements[1]);
        var elseAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ElseBranch.Statements[2]).Expression);
        Assert.Equal("_ternary", Assert.IsType<Identifier>(elseAssignment.Left).Name);
        Assert.NotNull(elseGuard.ThenBranch.Statements.OfType<Return>().SingleOrDefault());
    }

    [Fact]
    public void Generates_ErrorPropagation_MatchesIssueExample()
    {
        var rendered = Utility.GetLuauAST(
            """
            fn some_other_unsafe_fn(): Result<number, string> {
                return Result::ok(1);
            }
            fn unsafe_fn(): Result<number, string> {
                let value = some_other_unsafe_fn()?;
                return Result::ok(69 + value);
            }
            """,
            true
        ).Render();

        Assert.Contains("const _result = some_other_unsafe_fn()", rendered);
        Assert.Contains("if not _result.ok then", rendered);
        Assert.Contains("return _result", rendered);
        Assert.Contains("const value = _result.value", rendered);
        Assert.Contains("return { ok = true, value = 69 + value }", rendered);
    }

    [Fact]
    public void Generates_ErrorPropagation_CachesReceiverOnlyOnce()
    {
        // A side-effecting receiver (here, just any non-identifier expression) must be evaluated exactly
        // once - PushToVariable caching, same discipline as #153/#154 - not once for the nil/'ok' check
        // and again for '.value'.
        var luauTree = Utility.GetLuauAST(
            """
            fn get(): Result<number, string> { return Result::ok(1); }
            fn use_it(): Result<number, string> {
                let value = get()?;
                return Result::ok(value);
            }
            """,
            true
        );

        var useIt = luauTree.Statements.OfType<Function>().Single(f => f.Name == "use_it");
        var cached = Assert.IsType<ConstVariable>(useIt.Body.Statements[0]);
        Assert.Equal("_result", cached.Name);
        var call = Assert.IsType<Call>(cached.Initializer);
        Assert.Equal("get", Assert.IsType<Identifier>(call.Callee).Name);

        var guard = Assert.IsType<IfStatement>(useIt.Body.Statements[1]);
        var condition = Assert.IsType<UnaryOperator>(guard.Condition);
        Assert.Equal("not ", condition.Operator);
        var okAccess = Assert.IsType<PropertyAccess>(condition.Operand);
        Assert.Equal("_result", Assert.IsType<Identifier>(okAccess.Target).Name);
        Assert.Equal(["ok"], okAccess.Names);

        var returnStatement = Assert.IsType<Return>(Assert.Single(guard.ThenBranch.Statements));
        Assert.Equal("_result", Assert.IsType<Identifier>(returnStatement.Expression).Name);

        var valueLocal = Assert.IsType<ConstVariable>(useIt.Body.Statements[2]);
        var valueAccess = Assert.IsType<PropertyAccess>(valueLocal.Initializer);
        Assert.Equal("_result", Assert.IsType<Identifier>(valueAccess.Target).Name);
        Assert.Equal(["value"], valueAccess.Names);
    }

    [Fact]
    public void Generates_ForLoop_OverArray()
    {
        var luauTree = Utility.GetLuauAST("for x : [1, 2, 3] { }", true);
        Assert.Single(luauTree.Statements);
        var forStmt = Assert.IsType<ForStatement>(luauTree.Statements.First());
        Assert.Equal(2, forStmt.Names.Count);
        Assert.Equal("x", forStmt.Names.Last());
        var collection = Assert.IsType<Table>(forStmt.Expression);
        Assert.Equal(3, collection.Initializers.Count);
        Assert.Empty(forStmt.Body.Statements);
    }

    [Fact]
    public void Generates_ForLoop_OverArray_WithBlockBody()
    {
        var luauTree = Utility.GetLuauAST("for x : [1] { let y = x; }", true);
        Assert.Single(luauTree.Statements);
        var forStmt = Assert.IsType<ForStatement>(luauTree.Statements.First());
        Assert.Single(forStmt.Body.Statements);
        var constVar = Assert.IsType<ConstVariable>(forStmt.Body.Statements.First());
        Assert.Equal("y", constVar.Name);
    }

    [Fact]
    public void Generates_ForLoop_OverArray_WithBreak()
    {
        var luauTree = Utility.GetLuauAST("for x : [1] { break }", true);
        Assert.Single(luauTree.Statements);

        var forStmt = Assert.IsType<ForStatement>(luauTree.Statements.First());
        Assert.Single(forStmt.Body.Statements);
        Assert.IsType<Break>(forStmt.Body.Statements.First());
    }

    [Fact]
    public void Generates_ForLoop_OverArray_WithContinue()
    {
        var luauTree = Utility.GetLuauAST("for x : [1] { continue }", true);
        Assert.Single(luauTree.Statements);

        var forStmt = Assert.IsType<ForStatement>(luauTree.Statements.First());
        Assert.Single(forStmt.Body.Statements);
        Assert.IsType<Continue>(forStmt.Body.Statements.First());
    }

    [Fact]
    public void Generates_ForLoop_OverRangeLiteral()
    {
        var luauTree = Utility.GetLuauAST("for i : 0..5 { }", true);
        Assert.Single(luauTree.Statements);

        var numericFor = Assert.IsType<NumericForStatement>(luauTree.Statements.First());
        Assert.Equal("i", numericFor.Name);

        var start = Assert.IsType<NumberLiteral>(numericFor.Start);
        var end = Assert.IsType<NumberLiteral>(numericFor.End);
        Assert.Equal(0, start.Value);
        Assert.Equal(5, end.Value);
        Assert.Null(numericFor.IncrementBy);
        Assert.Empty(numericFor.Body.Statements);
    }

    [Fact]
    public void Generates_ForLoop_OverRangeLiteral_Descending()
    {
        var luauTree = Utility.GetLuauAST("for i : 5..0 { }", true);
        Assert.Single(luauTree.Statements);

        var numericFor = Assert.IsType<NumericForStatement>(luauTree.Statements.First());
        Assert.Equal("i", numericFor.Name);

        var start = Assert.IsType<NumberLiteral>(numericFor.Start);
        var end = Assert.IsType<NumberLiteral>(numericFor.End);
        Assert.Equal(5, start.Value);
        Assert.Equal(0, end.Value);

        var inc = Assert.IsType<UnaryOperator>(numericFor.IncrementBy);
        Assert.Equal("-", inc.Operator);
        var one = Assert.IsType<NumberLiteral>(inc.Operand);
        Assert.Equal(1, one.Value);
    }

    [Fact]
    public void Generates_ForLoop_OverRangeLiteral_ComplexStep()
    {
        var luauTree = Utility.GetLuauAST("let a = 1; let b = 10; for i : a..b { }", true);
        Assert.Equal(3, luauTree.Statements.Count);

        var numericFor = Assert.IsType<NumericForStatement>(luauTree.Statements.Last());
        Assert.Equal("i", numericFor.Name);
        Assert.IsType<Identifier>(numericFor.Start);
        Assert.IsType<Identifier>(numericFor.End);

        var ifExpr = Assert.IsType<IfExpression>(numericFor.IncrementBy);
        Assert.IsType<BinaryOperator>(ifExpr.Condition);
        var neg = Assert.IsType<UnaryOperator>(ifExpr.ThenBranch);
        Assert.Equal("-", neg.Operator);
        var pos = Assert.IsType<NumberLiteral>(ifExpr.ElseBranch);
        Assert.Equal(1, pos.Value);
    }

    [Fact]
    public void Generates_ForLoop_OverRangeVariable()
    {
        var luauTree = Utility.GetLuauAST("let r = 1..10; for i : r { }", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var numericFor = Assert.IsType<NumericForStatement>(luauTree.Statements.Last());
        Assert.Equal("i", numericFor.Name);
        var start = Assert.IsType<PropertyAccess>(numericFor.Start);
        var end = Assert.IsType<PropertyAccess>(numericFor.End);
        Assert.Equal("r", ((Identifier)start.Target).Name);
        Assert.Equal("minimum", start.Names.First());
        Assert.Equal("r", ((Identifier)end.Target).Name);
        Assert.Equal("maximum", end.Names.First());

        var ifExpression = Assert.IsType<IfExpression>(numericFor.IncrementBy);
        Assert.Empty(ifExpression.ElseIfBranches);
        Assert.NotNull(ifExpression.ElseBranch);

        var condition = Assert.IsType<BinaryOperator>(ifExpression.Condition);
        Assert.Equal("<", condition.Operator);
        Assert.IsType<PropertyAccess>(condition.Left);
        Assert.IsType<PropertyAccess>(condition.Right);

        var negativeOne = Assert.IsType<UnaryOperator>(ifExpression.ThenBranch);
        Assert.IsType<NumberLiteral>(negativeOne.Operand);
        Assert.IsType<NumberLiteral>(ifExpression.ElseBranch);
    }

    [Fact]
    public void Generates_ForLoop_Nested()
    {
        const string source = """
                    let xs = [1, 2]
                    for x : xs {
                        for y : xs { }
                    }
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(2, luauTree.Statements.Count);

        var outerFor = Assert.IsType<ForStatement>(luauTree.Statements.Last());
        Assert.Equal(2, outerFor.Names.Count);
        Assert.Equal("x", outerFor.Names.Last());

        var innerFor = Assert.IsType<ForStatement>(outerFor.Body.Statements.First());
        Assert.Equal(2, innerFor.Names.Count);
        Assert.Equal("y", innerFor.Names.Last());

        var identifier = Assert.IsType<Identifier>(innerFor.Expression);
        Assert.Equal("xs", identifier.Name);
    }

    [Fact]
    public void Generates_AfterStatement_WithCallExpressionBody()
    {
        var luauTree = Utility.GetLuauAST("after 1s foo(69)");
        Assert.Single(luauTree.Statements);

        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.First());
        var call = Assert.IsType<Call>(exprStmt.Expression);
        var propAccess = Assert.IsType<PropertyAccess>(call.Callee);
        var target = Assert.IsType<Identifier>(propAccess.Target);
        Assert.Equal("task", target.Name);
        Assert.Single(propAccess.Names);
        Assert.Equal("delay", propAccess.Names.First());
        Assert.Equal(3, call.Arguments.Count);

        var duration = Assert.IsType<NumberLiteral>(call.Arguments[0]);
        Assert.Equal(1, duration.Value);

        var fnIdentifier = Assert.IsType<Identifier>(call.Arguments[1]);
        Assert.Equal("foo", fnIdentifier.Name);

        var argument = Assert.IsType<NumberLiteral>(call.Arguments[2]);
        Assert.Equal(69, argument.Value);
    }

    [Fact]
    public void Generates_AfterStatement_WithBlockBody()
    {
        var luauTree = Utility.GetLuauAST("after 2s { foo(); bar() }");
        Assert.Single(luauTree.Statements);

        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.First());
        var call = Assert.IsType<Call>(exprStmt.Expression);
        var propAccess = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("task", ((Identifier)propAccess.Target).Name);
        Assert.Equal("delay", propAccess.Names.First());

        Assert.Equal(2, call.Arguments.Count);
        var duration = Assert.IsType<NumberLiteral>(call.Arguments.First());
        Assert.Equal(2, duration.Value);

        var anonFn = Assert.IsType<AnonymousFunction>(call.Arguments.Last());
        Assert.Empty(anonFn.Parameters);
        Assert.Equal(2, anonFn.Body.Statements.Count);

        var firstStmt = Assert.IsType<Call>(Assert.IsType<ExpressionStatement>(anonFn.Body.Statements.First()).Expression);
        var secondStmt = Assert.IsType<Call>(Assert.IsType<ExpressionStatement>(anonFn.Body.Statements.Last()).Expression);
        Assert.Equal("foo", ((Identifier)firstStmt.Callee).Name);
        Assert.Equal("bar", ((Identifier)secondStmt.Callee).Name);
    }

    [Fact]
    public void Generates_AfterStatement_WithComplexDuration()
    {
        var luauTree = Utility.GetLuauAST("after x + 1 { }");
        Assert.Single(luauTree.Statements);

        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.First());
        var call = Assert.IsType<Call>(exprStmt.Expression);
        var propAccess = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("task", ((Identifier)propAccess.Target).Name);
        Assert.Equal("delay", propAccess.Names.First());

        var duration = Assert.IsType<BinaryOperator>(call.Arguments.First());
        Assert.Equal("+", duration.Operator);
        Assert.IsType<Identifier>(duration.Left);
        Assert.IsType<NumberLiteral>(duration.Right);

        var anonFn = Assert.IsType<AnonymousFunction>(call.Arguments.Last());
        Assert.Empty(anonFn.Body.Statements);
    }

    [Fact]
    public void Generates_AfterStatement_WithNestedAfter()
    {
        var luauTree = Utility.GetLuauAST("after 1s { after 2s foo() }");
        Assert.Single(luauTree.Statements);

        var outerCall = Assert.IsType<Call>(Assert.IsType<ExpressionStatement>(luauTree.Statements.First()).Expression);
        Assert.Equal(4, outerCall.Arguments.Count);

        var outerDuration = Assert.IsType<NumberLiteral>(outerCall.Arguments[0]);
        Assert.Equal(1, outerDuration.Value);

        var outerProperty = Assert.IsType<PropertyAccess>(outerCall.Arguments[1]);
        Assert.Equal("task", Assert.IsType<Identifier>(outerProperty.Target).Name);
        Assert.Equal("delay", outerProperty.Names.First());

        var innerDuration = Assert.IsType<NumberLiteral>(outerCall.Arguments[2]);
        Assert.Equal(2, innerDuration.Value);

        var fnIdentifier = Assert.IsType<Identifier>(outerCall.Arguments[3]);
        Assert.Equal("foo", fnIdentifier.Name);
    }

    [Fact]
    public void Generates_AfterStatement_WithVariableReferenceInBody()
    {
        var luauTree = Utility.GetLuauAST("let x = 42; after 1s { let y = x + 69; print(y) }", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var varDecl = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        Assert.Equal("x", varDecl.Name);

        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(exprStmt.Expression);
        var anonFn = Assert.IsType<AnonymousFunction>(call.Arguments.Last());
        Assert.Equal(2, anonFn.Body.Statements.Count);
        Assert.IsType<ConstVariable>(anonFn.Body.Statements.First());

        var callStatement = Assert.IsType<ExpressionStatement>(anonFn.Body.Statements.Last());
        var printCall = Assert.IsType<Call>(callStatement.Expression);
        var callee = Assert.IsType<Identifier>(printCall.Callee);
        Assert.Equal("print", callee.Name);
        Assert.Single(printCall.Arguments);
        var arg = Assert.IsType<Identifier>(printCall.Arguments.First());
        Assert.Equal("y", arg.Name);
    }

    [Fact]
    public void Generates_AfterStatement_WithReturnInside()
    {
        var luauTree = Utility.GetLuauAST("fn test() { after 1s { return 42 } }", true);
        Assert.Single(luauTree.Statements);

        var fn = Assert.IsType<Function>(luauTree.Statements.First());
        Assert.Single(fn.Body.Statements);

        var afterStmt = Assert.IsType<ExpressionStatement>(fn.Body.Statements.First());
        var call = Assert.IsType<Call>(afterStmt.Expression);
        var anonFn = Assert.IsType<AnonymousFunction>(call.Arguments[1]);
        Assert.Single(anonFn.Body.Statements);

        var returnStmt = Assert.IsType<Return>(anonFn.Body.Statements.First());
        var returnValue = Assert.IsType<NumberLiteral>(returnStmt.Expression);
        Assert.Equal(42, returnValue.Value);
    }

    [Fact]
    public void Generates_EveryStatement_WithCallExpressionBody()
    {
        var luauTree = Utility.GetLuauAST("every 1s foo(69)");
        Assert.Single(luauTree.Statements);

        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.First());
        var call = Assert.IsType<Call>(exprStmt.Expression);
        var propAccess = Assert.IsType<PropertyAccess>(call.Callee);
        var target = Assert.IsType<Identifier>(propAccess.Target);
        Assert.Equal("Loom", target.Name);
        Assert.Single(propAccess.Names);
        Assert.Equal("every", propAccess.Names.First());
        Assert.Equal(4, call.Arguments.Count);

        var duration = Assert.IsType<NumberLiteral>(call.Arguments[0]);
        Assert.Equal(1, duration.Value);

        Assert.IsType<NilLiteral>(call.Arguments[1]);

        var fnIdentifier = Assert.IsType<Identifier>(call.Arguments[2]);
        Assert.Equal("foo", fnIdentifier.Name);

        var argument = Assert.IsType<NumberLiteral>(call.Arguments[3]);
        Assert.Equal(69, argument.Value);
    }

    [Fact]
    public void Generates_EveryStatement_WithBlockBody()
    {
        var luauTree = Utility.GetLuauAST("every 2s { foo(); bar() }");
        Assert.Single(luauTree.Statements);

        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.First());
        var call = Assert.IsType<Call>(exprStmt.Expression);
        var propAccess = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("Loom", ((Identifier)propAccess.Target).Name);
        Assert.Equal("every", propAccess.Names.First());

        Assert.Equal(3, call.Arguments.Count);
        var duration = Assert.IsType<NumberLiteral>(call.Arguments[0]);
        Assert.Equal(2, duration.Value);

        Assert.IsType<NilLiteral>(call.Arguments[1]);

        var anonFn = Assert.IsType<AnonymousFunction>(call.Arguments[2]);
        Assert.Empty(anonFn.Parameters);
        Assert.Equal(2, anonFn.Body.Statements.Count);

        var firstStmt = Assert.IsType<Call>(Assert.IsType<ExpressionStatement>(anonFn.Body.Statements.First()).Expression);
        var secondStmt = Assert.IsType<Call>(Assert.IsType<ExpressionStatement>(anonFn.Body.Statements.Last()).Expression);
        Assert.Equal("foo", ((Identifier)firstStmt.Callee).Name);
        Assert.Equal("bar", ((Identifier)secondStmt.Callee).Name);
    }

    [Fact]
    public void Generates_EveryStatement_WithCondition()
    {
        var luauTree = Utility.GetLuauAST("every 1s while isActive foo()");
        Assert.Single(luauTree.Statements);

        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.First());
        var call = Assert.IsType<Call>(exprStmt.Expression);
        Assert.Equal(3, call.Arguments.Count);

        var conditionFn = Assert.IsType<AnonymousFunction>(call.Arguments[1]);
        Assert.Empty(conditionFn.Parameters);
        Assert.Null(conditionFn.ReturnType);

        var returnStmt = Assert.IsType<Return>(Assert.Single(conditionFn.Body.Statements));
        var conditionIdentifier = Assert.IsType<Identifier>(returnStmt.Expression);
        Assert.Equal("isActive", conditionIdentifier.Name);

        var fnIdentifier = Assert.IsType<Identifier>(call.Arguments[2]);
        Assert.Equal("foo", fnIdentifier.Name);
    }

    [Fact]
    public void Generates_WhileLoop_WithBlockBody()
    {
        var luauTree = Utility.GetLuauAST("while true { break }");
        Assert.Single(luauTree.Statements);

        var whileStatement = Assert.IsType<WhileStatement>(luauTree.Statements.First());
        var condition = Assert.IsType<BooleanLiteral>(whileStatement.Condition);
        Assert.True(condition.Value);

        var body = whileStatement.Body;
        Assert.Single(body.Statements);
        Assert.IsType<Break>(body.Statements.First());
    }

    [Fact]
    public void Generates_WhileLoop_WithExpressionBody()
    {
        var luauTree = Utility.GetLuauAST("while true continue");
        Assert.Single(luauTree.Statements);

        var whileStatement = Assert.IsType<WhileStatement>(luauTree.Statements.First());
        var body = whileStatement.Body;
        Assert.Single(body.Statements);
        Assert.IsType<Continue>(body.Statements.First());
    }

    [Fact]
    public void Generates_Break()
    {
        var luauTree = Utility.GetLuauAST("while true { break }");
        var whileStatement = Assert.IsType<WhileStatement>(luauTree.Statements.First());
        var block = whileStatement.Body;
        var breakStmt = Assert.IsType<Break>(block.Statements.First());
        Assert.Equal("break", breakStmt.Render());
    }

    [Fact]
    public void Generates_Continue()
    {
        var luauTree = Utility.GetLuauAST("while true { continue }");
        var whileStatement = Assert.IsType<WhileStatement>(luauTree.Statements.First());
        var block = whileStatement.Body;
        var continueStmt = Assert.IsType<Continue>(block.Statements.First());
        Assert.Equal("continue", continueStmt.Render());
    }

    [Fact]
    public void Generates_NestedWhileLoops_WithBreakAndContinue()
    {
        var luauTree = Utility.GetLuauAST(
            """
                    while a {
                        while b {
                            break
                        }
                        continue
                    }
            """
        );

        Assert.Single(luauTree.Statements);

        var outerWhile = Assert.IsType<WhileStatement>(luauTree.Statements.First());
        var outerBody = outerWhile.Body;
        Assert.Equal(2, outerBody.Statements.Count);

        var innerWhile = Assert.IsType<WhileStatement>(outerBody.Statements.First());
        var innerBody = innerWhile.Body;
        Assert.Single(innerBody.Statements);
        Assert.IsType<Break>(innerBody.Statements.First());

        var outerContinue = Assert.IsType<Continue>(outerBody.Statements[1]);
        Assert.Equal("continue", outerContinue.Render());
    }

    [Fact]
    public void Generates_Interface_With_Constraint_And_Implementation()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Display { fn display(): void; }

            interface Base {
                value: number
            }

            interface Container: Base { }

            implement Display for Container {
                fn display() -> print(value);
            }
            """,
            true
        );

        Assert.Equal(7, luauTree.Statements.Count);

        var alias = Assert.IsType<TypeAlias>(luauTree.Statements[2]);
        var intersection = Assert.IsType<IntersectionType>(alias.Type);
        Assert.Equal(3, intersection.Types.Count);

        Assert.Equal("Base", Assert.IsType<TypeName>(intersection.Types[0]).Name);
        Assert.IsType<TableType>(intersection.Types[1]);
        Assert.Equal("Display", Assert.IsType<TypeName>(intersection.Types[2]).Name);
    }

    [Fact]
    public void Generates_Implement_Multiple()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Display { fn display(): void }
            trait Serialize { fn serialize(): string }

            interface Container { value: number }

            implement Display for Container {
                fn display() -> print(value);
            }

            implement Serialize for Container {
                fn serialize() -> string(value);
            }

            let container = new Container { value: 69 };
            """,
            true
        );

        Assert.Equal(13, luauTree.Statements.Count);

        var interfaceAlias = Assert.IsType<TypeAlias>(luauTree.Statements[2]);
        var intersection = Assert.IsType<IntersectionType>(interfaceAlias.Type);
        Assert.Equal(3, intersection.Types.Count);
        Assert.IsType<TableType>(intersection.Types[0]);
        Assert.Equal("Display", Assert.IsType<TypeName>(intersection.Types[1]).Name);
        Assert.Equal("Serialize", Assert.IsType<TypeName>(intersection.Types[2]).Name);
        Assert.Equal("Display_for_Container", Assert.IsType<LocalVariable>(luauTree.Statements[3]).Name);
        Assert.Equal("Serialize_for_Container", Assert.IsType<LocalVariable>(luauTree.Statements[7]).Name);

        // the merged metatable is computed once, right after both trait tables it merges are in scope -
        // not re-computed at every construction site.
        var mergedLocal = Assert.IsType<LocalVariable>(luauTree.Statements[11]);
        Assert.Equal("Container_meta", mergedLocal.Name);
        var mergeCall = Assert.IsType<Call>(mergedLocal.Initializer);
        Assert.Equal(2, mergeCall.Arguments.Count);
        Assert.Equal("Display_for_Container", Assert.IsType<Identifier>(mergeCall.Arguments[0]).Name);
        Assert.Equal("Serialize_for_Container", Assert.IsType<Identifier>(mergeCall.Arguments[1]).Name);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[12]);
        var cast = Assert.IsType<TypeCast>(variable.Initializer);
        var setmetatableCall = Assert.IsType<Call>(cast.Expression);
        Assert.Equal("Container_meta", Assert.IsType<Identifier>(setmetatableCall.Arguments[1]).Name);
    }

    [Fact]
    public void Generates_MergedMetatable_SharedAcrossMultipleConstructionSites_NotRecomputedPerSite()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Display { fn display(): void }
            trait Serialize { fn serialize(): string }

            interface Container { value: number }

            implement Display for Container {
                fn display() -> print(value);
            }

            implement Serialize for Container {
                fn serialize() -> string(value);
            }

            let first = new Container { value: 1 };
            let second = new Container { value: 2 };
            let third = first with { value: 3 };
            """,
            true
        );

        var mergeCalls = luauTree.Statements.OfType<LocalVariable>().Where(v => v.Initializer is Call { Callee: PropertyAccess { Names: ["merge_meta"] } }).ToList();
        Assert.Single(mergeCalls);

        var constructions = luauTree.Statements.OfType<ConstVariable>().Where(v => v.Initializer is TypeCast).ToList();
        Assert.Equal(3, constructions.Count);
        foreach (var construction in constructions)
        {
            var cast = Assert.IsType<TypeCast>(construction.Initializer);
            var setmetatableCall = Assert.IsType<Call>(cast.Expression);
            Assert.Equal(mergeCalls[0].Name, Assert.IsType<Identifier>(setmetatableCall.Arguments[1]).Name);
        }
    }

    /// <summary>
    ///     A shared merged metatable must be a TOP-LEVEL local, never one built lazily inside whichever
    ///     function body happens to construct the interface first - two sibling functions each constructing
    ///     the same multi-trait interface must both see (and both reference) the exact same local, since a
    ///     local declared inside one function's body would be out of scope, and therefore an undeclared
    ///     global, from inside the other.
    /// </summary>
    [Fact]
    public void Generates_MergedMetatable_AtTopLevel_VisibleAcrossSiblingFunctionBodies()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Display { fn display(): void }
            trait Serialize { fn serialize(): string }

            interface Container { value: number }

            implement Display for Container {
                fn display() -> print(value);
            }

            implement Serialize for Container {
                fn serialize() -> string(value);
            }

            fn make_one(): Container -> new Container { value: 1 };
            fn make_two(): Container -> new Container { value: 2 };
            """,
            true
        );

        var mergedLocal = Assert.Single(luauTree.Statements.OfType<LocalVariable>(), v => v.Initializer is Call { Callee: PropertyAccess { Names: ["merge_meta"] } });

        var functions = luauTree.Statements.OfType<Function>().Where(f => f.Name is "make_one" or "make_two").ToList();
        Assert.Equal(2, functions.Count);
        foreach (var function in functions)
        {
            var @return = Assert.IsType<Return>(Assert.Single(function.Body.Statements));
            var cast = Assert.IsType<TypeCast>(@return.Expression);
            var setmetatableCall = Assert.IsType<Call>(cast.Expression);
            Assert.Equal(mergedLocal.Name, Assert.IsType<Identifier>(setmetatableCall.Arguments[1]).Name);
        }
    }

    [Fact]
    public void Generates_TraitDefault_SharedAcrossImplementations_ViaDirectFieldAssignment()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Greeting { fn greet(): string -> "hi"; }
            interface A { }
            interface B { }
            implement Greeting for A { }
            implement Greeting for B { }
            """,
            true
        );

        var sharedDefaults = luauTree.Statements.OfType<Function>().Where(f => f.Name == "Greeting_greet_default").ToList();
        Assert.Single(sharedDefaults);

        var assignments = luauTree.Statements
            .OfType<ExpressionStatement>()
            .Select(s => s.Expression)
            .OfType<BinaryOperator>()
            .Where(b => b is { Operator: "=", Left: PropertyAccess { Names: ["greet"] } })
            .ToList();

        Assert.Equal(2, assignments.Count);
        foreach (var assignment in assignments)
            Assert.Equal("Greeting_greet_default", Assert.IsType<Identifier>(assignment.Right).Name);
    }

    [Fact]
    public void Generates_Implement_OverridingDefault_EmitsInlineFunction_NoSharedDefault()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Greeting { fn greet(): string -> "hi"; }
            interface A { }
            implement Greeting for A { fn greet(): string -> "hello"; }
            """,
            true
        );

        Assert.DoesNotContain(luauTree.Statements.OfType<Function>(), f => f.Name == "Greeting_greet_default");
        Assert.Contains(luauTree.Statements.OfType<Function>(), f => f.Name == "Greeting_for_A.greet");
    }

    [Theory]
    [InlineData("Display", "to_string", "deep_display")]
    [InlineData("Eq", "equals", "deep_equal")]
    [InlineData("Hash", "hash", "deep_hash")]
    public void Generates_IntrinsicTraitDefault_AsInternalRuntimeCall_NeverCompilingItsOwnSource(
        string traitName,
        string methodName,
        string runtimeFunctionName)
    {
        var luauTree = Utility.GetLuauAST($"interface Foo {{ }} implement {traitName} for Foo {{ }}", true);
        var function = Assert.Single(luauTree.Statements.OfType<Function>(), f => f.Name == $"{traitName}_{methodName}_default");

        var @return = Assert.IsType<Return>(Assert.Single(function.Body.Statements));
        var call = Assert.IsType<Call>(@return.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(runtimeFunctionName, Assert.Single(callee.Names));
        Assert.Equal("self", Assert.IsType<Identifier>(call.Arguments[0]).Name);
    }

    [Fact]
    public void Generates_UserTrait_SameNameAsIntrinsic_DoesNotGetInternalRuntimeSubstitution()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Eq { fn equals(other: unknown): bool -> true; }
            interface Foo { }
            implement Eq for Foo { }
            """,
            true
        );

        var function = Assert.Single(luauTree.Statements.OfType<Function>(), f => f.Name == "Eq_equals_default");
        var @return = Assert.IsType<Return>(Assert.Single(function.Body.Statements));
        Assert.IsType<BooleanLiteral>(@return.Expression);
    }

    [Fact]
    public void ThrowsFor_DefaultMethod_OnGenericTrait()
    {
        const string source = """
            trait Wrap<T> { fn identity(x: T): T -> x; }
            interface Foo { }
            implement Wrap<number> for Foo { }
            """;

        var diagnostics = Utility.GetGeneratorDiagnostics(source, true);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotImplemented,
            "A default method on generic trait 'Wrap' is not yet supported.",
            "override it explicitly in every 'implement' block instead."
        );
    }

    [Fact]
    public void Generates_InterfaceInvocation_WithSingleImplementation_OmitsMergeMeta()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Serialize<T> { fn serialize: T }
            interface User { name: string, age: number }
            implement Serialize<string> for User {
                fn serialize -> name + ", " + string(age)
            }
            let user = new User { name: "Runic", age: 21 };
            """,
            true
        );

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var cast = Assert.IsType<TypeCast>(variable.Initializer);
        var setmetatableCall = Assert.IsType<Call>(cast.Expression);
        Assert.Equal(2, setmetatableCall.Arguments.Count);
        Assert.Equal("Serialize_string_for_User", Assert.IsType<Identifier>(setmetatableCall.Arguments[1]).Name);
    }

    [Theory]
    [InlineData(
        // single trait, construction precedes its only implement
        """
        interface Foo { }
        let a = new Foo { };
        implement Eq for Foo { }
        """
    )]
    [InlineData(
        // two traits, construction precedes the second implement
        """
        interface Foo { }
        implement Display for Foo { }
        let a = new Foo { };
        implement Eq for Foo { }
        """
    )]
    [InlineData(
        // the 'with' operator is a construction site too
        """
        interface Foo { }
        implement Display for Foo { }
        let a = new Foo { };
        let b = a with { };
        implement Eq for Foo { }
        """
    )]
    public void ThrowsFor_Construction_BeforeAllOfItsInterfaceImplementBlocks(string source)
    {
        // Regression test: Luau does not hoist the 'local X_for_Y' a trait's table needs, unlike Loom's
        // own type checker hoisting 'implement' blocks - a construction site earlier in the file than
        // one of its interface's traits used to silently reference that trait's table before its own
        // declaration, resolving to an undeclared global 'nil' and dropping the trait's methods with no
        // diagnostic at all.
        var diagnostics = Utility.GetGeneratorDiagnostics(source, true);
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.ConstructedBeforeImplement);
    }

    [Fact]
    public void Allows_Construction_AfterAllOfItsInterfaceImplementBlocks() =>
        Utility.AssertNoErrors(
            Utility.GetGeneratorDiagnostics(
                """
                interface Foo { }
                implement Display for Foo { }
                implement Eq for Foo { }
                let a = new Foo { };
                """,
                true
            )
        );

    [Fact]
    public void Generates_Implement_Basic()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Display { fn display(depth: number): void }
            interface Container { value: number }
            implement Display for Container {
                fn display(depth) -> print(depth * value);
            }
            let container = new Container { value: 69 };
            container.display(420);
            """,
            true
        );

        Assert.Equal(8, luauTree.Statements.Count);

        var interfaceTypeAlias = Assert.IsType<TypeAlias>(luauTree.Statements[1]);
        Assert.Equal("Container", interfaceTypeAlias.Name);
        Assert.Empty(interfaceTypeAlias.TypeParameters.Parameters);

        var intersection = Assert.IsType<IntersectionType>(interfaceTypeAlias.Type);
        Assert.Equal(2, intersection.Types.Count);
        Assert.IsType<TableType>(intersection.Types.First());

        var traitTypeName = Assert.IsType<TypeName>(intersection.Types.Last());
        Assert.Equal("Display", traitTypeName.Name);
        Assert.NotNull(traitTypeName.TypeArguments);
        Assert.Empty(traitTypeName.TypeArguments);

        const string metaName = "Display_for_Container";
        var implementationVariable = Assert.IsType<LocalVariable>(luauTree.Statements[2]);
        Assert.Equal(metaName, implementationVariable.Name);
        Assert.IsType<Table>(implementationVariable.Initializer);

        var indexAssignmentStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[3]);
        var indexAssignment = Assert.IsType<BinaryOperator>(indexAssignmentStatement.Expression);
        Assert.Equal("=", indexAssignment.Operator);

        var indexAccess = Assert.IsType<PropertyAccess>(indexAssignment.Left);
        var identifier = Assert.IsType<Identifier>(indexAccess.Target);
        var rightIdentifier = Assert.IsType<Identifier>(indexAssignment.Right);
        Assert.Equal("__index", Assert.Single(indexAccess.Names));
        Assert.Equal(metaName, identifier.Name);
        Assert.Equal(metaName, rightIdentifier.Name);

        var castAssignmentStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[4]);
        var castAssignment = Assert.IsType<BinaryOperator>(castAssignmentStatement.Expression);
        Assert.Equal("=", castAssignment.Operator);

        var castIdentifier = Assert.IsType<Identifier>(castAssignment.Left);
        var cast = Assert.IsType<TypeCast>(castAssignment.Right);
        var castedIdentifier = Assert.IsType<Identifier>(cast.Expression);
        var castType = Assert.IsType<TypeName>(cast.Type);
        Assert.Equal(metaName, castIdentifier.Name);
        Assert.Equal(metaName, castedIdentifier.Name);
        Assert.Equal("Container", castType.Name);

        var displayFunction = Assert.IsType<Function>(luauTree.Statements[5]);
        Assert.False(displayFunction.IsConst);
        Assert.Equal($"{metaName}.display", displayFunction.Name);
        Assert.Equal(2, displayFunction.Parameters.Count);

        var selfParameter = displayFunction.Parameters.First();
        Assert.Equal("self", selfParameter.Name);

        var selfType = Assert.IsType<TypeName>(selfParameter.DeclaredType);
        Assert.Equal("Container", selfType.Name);
        Assert.NotNull(selfType.TypeArguments);
        Assert.Empty(selfType.TypeArguments);
        Assert.Equal("depth", displayFunction.Parameters.Last().Name);

        var @return = Assert.IsType<Return>(Assert.Single(displayFunction.Body.Statements));
        var printCall = Assert.IsType<Call>(@return.Expression);
        Assert.Equal("print", Assert.IsType<Identifier>(printCall.Callee).Name);

        var binaryOperator = Assert.IsType<BinaryOperator>(Assert.Single(printCall.Arguments));
        Assert.Equal("*", binaryOperator.Operator);
        Assert.Equal("depth", Assert.IsType<Identifier>(binaryOperator.Left).Name);

        var selfAccess = Assert.IsType<PropertyAccess>(binaryOperator.Right);
        Assert.Equal("self", Assert.IsType<Identifier>(selfAccess.Target).Name);
        Assert.Equal("value", Assert.Single(selfAccess.Names));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[6]);
        Assert.Equal("container", variable.Name);
        Assert.Null(variable.DeclaredType);

        var constructorCast = Assert.IsType<TypeCast>(variable.Initializer);
        var constructorCastType = Assert.IsType<TypeName>(constructorCast.Type);
        Assert.Equal("Container", constructorCastType.Name);
        Assert.NotNull(constructorCastType.TypeArguments);
        Assert.Empty(constructorCastType.TypeArguments);

        var setmetatableCall = Assert.IsType<Call>(constructorCast.Expression);
        Assert.False(setmetatableCall.IsMethod);
        Assert.Equal("setmetatable", Assert.IsType<Identifier>(setmetatableCall.Callee).Name);
        Assert.Equal(2, setmetatableCall.Arguments.Count);
        Assert.IsType<Table>(setmetatableCall.Arguments.First());

        Assert.Equal(metaName, Assert.IsType<Identifier>(setmetatableCall.Arguments.Last()).Name);

        var methodCallStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[7]);
        var methodCall = Assert.IsType<Call>(methodCallStatement.Expression);
        var methodAccess = Assert.IsType<PropertyAccess>(methodCall.Callee);
        Assert.True(methodCall.IsMethod);
        Assert.Equal("container", Assert.IsType<Identifier>(methodAccess.Target).Name);
        Assert.Equal("display", Assert.Single(methodAccess.Names));
        Assert.Equal(420, Assert.IsType<NumberLiteral>(Assert.Single(methodCall.Arguments)).Value);
    }

    [Fact]
    public void Generates_StaticBlock_TableWithFieldAndFunction()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Vector2 {
                x: number
                y: number
                static zero: Vector2
                static create: fn(x: number, y: number): Vector2
            }

            static Vector2 {
                zero = new Vector2 { x: 0, y: 0 };
                fn create(x, y) -> new Vector2 { x, y };
            }
            """,
            true
        );

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements[1]);
        Assert.Equal("Vector2", variable.Name);
        Assert.IsType<Table>(variable.Initializer);

        var fieldAssignmentStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[2]);
        var fieldAssignment = Assert.IsType<BinaryOperator>(fieldAssignmentStatement.Expression);
        Assert.Equal("=", fieldAssignment.Operator);

        var fieldAccess = Assert.IsType<PropertyAccess>(fieldAssignment.Left);
        Assert.Equal("Vector2", Assert.IsType<Identifier>(fieldAccess.Target).Name);
        Assert.Equal("zero", Assert.Single(fieldAccess.Names));
        Assert.IsType<Table>(fieldAssignment.Right);

        var createFunction = Assert.IsType<Function>(luauTree.Statements[3]);
        Assert.Equal("Vector2.create", createFunction.Name);
        Assert.Equal(2, createFunction.Parameters.Count);
        Assert.Equal(["x", "y"], createFunction.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void Generates_AmbientInterfaceWithStatics_EmitsOnlyTheTypeAlias()
    {
        var luauTree = Utility.GetLuauAST(
            """
            declare interface Vector2 {
                static zero: Vector2
                static create: fn(x: number, y: number): Vector2
            }

            let v = Vector2::create(1, 2);
            """,
            true
        );

        Assert.Single(luauTree.Statements.OfType<TypeAlias>());
        Assert.Empty(luauTree.Statements.OfType<LocalVariable>());
        Assert.Empty(luauTree.Statements.OfType<Function>());
    }

    [Fact]
    public void Generates_SelfExpression_AsElementAccessOnSelf()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface WithIndexer { [string]: number }
            trait GetValue<K, V> { fn get_value(key: K): V }
            implement GetValue<string, number> for WithIndexer {
                fn get_value(key) -> @[key];
            }
            """,
            true
        );

        var getValueFunction = Assert.IsType<Function>(luauTree.Statements[5]);
        Assert.Equal("GetValue_string_number_for_WithIndexer.get_value", getValueFunction.Name);

        var @return = Assert.IsType<Return>(Assert.Single(getValueFunction.Body.Statements));
        var elementAccess = Assert.IsType<ElementAccess>(@return.Expression);
        Assert.Equal("self", Assert.IsType<Identifier>(elementAccess.Target).Name);
        Assert.Equal("key", Assert.IsType<Identifier>(elementAccess.Index).Name);
    }

    [Fact]
    public void Generates_SelfExpression_AsPropertyAccessOnSelf()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Container { value: number }
            trait Display { fn display(): void }
            implement Display for Container {
                fn display() -> print(@.value);
            }
            """,
            true
        );

        var displayFunction = Assert.IsType<Function>(luauTree.Statements[5]);
        var @return = Assert.IsType<Return>(Assert.Single(displayFunction.Body.Statements));
        var printCall = Assert.IsType<Call>(@return.Expression);
        var selfAccess = Assert.IsType<PropertyAccess>(Assert.Single(printCall.Arguments));
        Assert.Equal("self", Assert.IsType<Identifier>(selfAccess.Target).Name);
        Assert.Equal("value", Assert.Single(selfAccess.Names));
    }

    [Fact]
    public void Generates_SelfExpression_CallsMethodFromOtherImplementedTrait_UsingSelfAndColonSyntax()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Container { value: number }
            trait Display { fn display: void }
            trait Balls { fn balls: void }

            implement Balls for Container {
                fn balls -> print(@.value);
            }

            implement Display for Container {
                fn display -> print(@.balls());
            }
            """,
            true
        );

        var displayFunction = Assert.IsType<Function>(luauTree.Statements[^1]);
        Assert.Equal("Display_for_Container.display", displayFunction.Name);

        var @return = Assert.IsType<Return>(Assert.Single(displayFunction.Body.Statements));
        var printCall = Assert.IsType<Call>(@return.Expression);
        var ballsCall = Assert.IsType<Call>(Assert.Single(printCall.Arguments));
        Assert.True(ballsCall.IsMethod);

        var ballsAccess = Assert.IsType<PropertyAccess>(ballsCall.Callee);
        Assert.Equal("self", Assert.IsType<Identifier>(ballsAccess.Target).Name);
        Assert.Equal("balls", Assert.Single(ballsAccess.Names));
    }

    [Fact]
    public void Generates_SelfExpression_BareRecursiveMethodCall_UsesSelfAndColonSyntax()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Container { value: number }
            trait Display { fn display: void }
            implement Display for Container {
                fn display -> print(display());
            }
            """,
            true
        );

        var displayFunction = Assert.IsType<Function>(luauTree.Statements[^1]);
        var @return = Assert.IsType<Return>(Assert.Single(displayFunction.Body.Statements));
        var printCall = Assert.IsType<Call>(@return.Expression);
        var recursiveCall = Assert.IsType<Call>(Assert.Single(printCall.Arguments));
        Assert.True(recursiveCall.IsMethod);

        var recursiveAccess = Assert.IsType<PropertyAccess>(recursiveCall.Callee);
        Assert.Equal("self", Assert.IsType<Identifier>(recursiveAccess.Target).Name);
        Assert.Equal("display", Assert.Single(recursiveAccess.Names));
    }

    [Fact]
    public void Generates_BareCall_ToMethodFromOtherImplementedTrait_UsingSelfAndColonSyntax()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Container { value: number }
            trait Display { fn display: void }
            trait Balls { fn balls: void }

            implement Balls for Container {
                fn balls -> print(@.value);
            }

            implement Display for Container {
                fn display -> print(balls());
            }
            """,
            true
        );

        var displayFunction = Assert.IsType<Function>(luauTree.Statements[^1]);
        var @return = Assert.IsType<Return>(Assert.Single(displayFunction.Body.Statements));
        var printCall = Assert.IsType<Call>(@return.Expression);
        var ballsCall = Assert.IsType<Call>(Assert.Single(printCall.Arguments));
        Assert.True(ballsCall.IsMethod);

        var ballsAccess = Assert.IsType<PropertyAccess>(ballsCall.Callee);
        Assert.Equal("self", Assert.IsType<Identifier>(ballsAccess.Target).Name);
        Assert.Equal("balls", Assert.Single(ballsAccess.Names));
    }

    [Fact]
    public void Generates_TraitDeclaration()
    {
        var luauTree = Utility.GetLuauAST("trait T { fn method(): number }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("T", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.Single(tableType.Properties);

        var prop = tableType.Properties.First();
        Assert.Equal("method", prop.Name);
        Assert.Null(prop.Visibility);

        var fnType = Assert.IsType<FunctionType>(prop.Type);
        Assert.Single(fnType.ParameterTypes);

        var returnType = Assert.IsType<PrimitiveType>(fnType.ReturnType);
        Assert.Equal(PrimitiveTypeKind.Number, returnType.Kind);
    }

    [Fact]
    public void Generates_TraitDeclaration_WithParameters()
    {
        var luauTree = Utility.GetLuauAST("trait T { fn method(x: number, y: string): bool }", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Single());
        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        var prop = tableType.Properties.Single();
        var fnType = Assert.IsType<FunctionType>(prop.Type);
        Assert.Equal(3, fnType.ParameterTypes.Count);
        Assert.IsType<TypeName>(fnType.ParameterTypes[0]);
        Assert.IsType<PrimitiveType>(fnType.ParameterTypes[1]);
        Assert.IsType<PrimitiveType>(fnType.ParameterTypes[2]);
        Assert.IsType<PrimitiveType>(fnType.ReturnType);
    }

    [Fact]
    public void Generates_TraitDeclaration_Generic()
    {
        var luauTree = Utility.GetLuauAST("trait Trait<T> { fn method(value: T): T }", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Single());
        Assert.Single(typeAlias.TypeParameters.Parameters);

        var param = typeAlias.TypeParameters.Parameters[0];
        Assert.Equal("T", param.Name);

        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        var prop = tableType.Properties.Single();
        var fnType = Assert.IsType<FunctionType>(prop.Type);
        Assert.Equal(2, fnType.ParameterTypes.Count);
        Assert.Null(prop.Visibility);

        var selfType = Assert.IsType<TypeName>(fnType.ParameterTypes[0]);
        Assert.Equal("Trait", selfType.Name);

        var typeArgument = Assert.IsType<TypeName>(Assert.Single(selfType.TypeArguments));
        Assert.Equal("T", typeArgument.Name);
        Assert.Empty(typeArgument.TypeArguments);

        var paramType = Assert.IsType<TypeName>(fnType.ParameterTypes[1]);
        Assert.Equal("T", paramType.Name);

        var returnType = Assert.IsType<TypeName>(fnType.ReturnType);
        Assert.Equal("T", returnType.Name);
    }

    [Fact]
    public void Generates_TraitDeclaration_MultipleMethods()
    {
        var luauTree = Utility.GetLuauAST("trait T { fn a(): number; fn b(): string }", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Single());
        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.Equal(2, tableType.Properties.Count);
        Assert.Equal("a", tableType.Properties[0].Name);
        Assert.Equal("b", tableType.Properties[1].Name);
        Assert.All(tableType.Properties, p => Assert.IsType<FunctionType>(p.Type));
    }

    [Fact]
    public void Generates_InterfaceInvocation_EmptyBody()
    {
        var luauTree = Utility.GetLuauAST("interface I { } new I {}", true);
        Assert.True(luauTree.Statements.Count >= 2);
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Empty(table.Initializers);
    }

    [Fact]
    public void Generates_InterfaceInvocation_PropertyInitializer()
    {
        var luauTree = Utility.GetLuauAST("interface I { x: number } new I { x: 1 }", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Single(table.Initializers);

        var propInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        Assert.Equal("x", propInit.PropertyName);

        var value = Assert.IsType<NumberLiteral>(propInit.Value);
        Assert.Equal(1, value.Value);
    }

    [Fact]
    public void Generates_InterfaceInvocation_ShorthandPropertyInitializer()
    {
        var luauTree = Utility.GetLuauAST("interface I { x: number } let x = 69; new I { x }", true);
        Assert.Equal(3, luauTree.Statements.Count);

        var propVariable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        Assert.Equal("x", propVariable.Name);
        Assert.Null(propVariable.DeclaredType);
        Assert.IsType<NumberLiteral>(propVariable.Initializer);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Single(table.Initializers);

        var propInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        Assert.Equal("x", propInit.PropertyName);
        Assert.Equal("x", Assert.IsType<Identifier>(propInit.Value).Name);
    }

    [Fact]
    public void Generates_InterfaceInvocation_IndexInitializer()
    {
        var luauTree = Utility.GetLuauAST("interface I { [number]: string } new I { [0]: 'hello' }", true);
        Assert.True(luauTree.Statements.Count >= 2);
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Single(table.Initializers);
        var indexInit = Assert.IsType<ComputedPropertyTableInitializer>(table.Initializers[0]);
        var indexValue = Assert.IsType<NumberLiteral>(indexInit.Key);
        Assert.Equal(0, indexValue.Value);
        var value = Assert.IsType<StringLiteral>(indexInit.Value);
        Assert.Equal("hello", value.Value);
    }

    [Fact]
    public void Generates_InterfaceInvocation_MixedInitializers()
    {
        var luauTree = Utility.GetLuauAST("interface I { x: number, [string]: bool } new I { x: 1, ['key']: true }", true);
        Assert.True(luauTree.Statements.Count >= 2);
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Equal(2, table.Initializers.Count);
        var propInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        Assert.Equal("x", propInit.PropertyName);
        var indexInit = Assert.IsType<ComputedPropertyTableInitializer>(table.Initializers[1]);
        var key = Assert.IsType<StringLiteral>(indexInit.Key);
        Assert.Equal("key", key.Value);
        var val = Assert.IsType<BooleanLiteral>(indexInit.Value);
        Assert.True(val.Value);
    }

    [Fact]
    public void Generates_WithOperator_OverridesListedField_ReadsOthersOffTheLeftOperand()
    {
        var luauTree = Utility.GetLuauAST(
            "interface I { x: number, y: string } let i = new I { x: 1, y: 'a' }; let j = i with { x: 2 }",
            true
        );
        Assert.Equal(3, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Equal(2, table.Initializers.Count);

        var x = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        Assert.Equal("x", x.PropertyName);
        Assert.Equal(2, Assert.IsType<NumberLiteral>(x.Value).Value);

        var y = Assert.IsType<PropertyTableInitializer>(table.Initializers[1]);
        Assert.Equal("y", y.PropertyName);
        var access = Assert.IsType<PropertyAccess>(y.Value);
        Assert.Equal("y", Assert.Single(access.Names));
        Assert.Equal("i", Assert.IsType<Identifier>(access.Target).Name);
    }

    [Fact]
    public void Generates_WithOperator_IndexInitializer_AddedAlongsideCarriedOverProperties()
    {
        var luauTree = Utility.GetLuauAST(
            "interface I { x: number, [string]: bool } let i = new I { x: 1, ['a']: true }; let j = i with { ['b']: false }",
            true
        );

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Equal(2, table.Initializers.Count);

        var x = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        Assert.Equal("x", x.PropertyName);
        var access = Assert.IsType<PropertyAccess>(x.Value);
        Assert.Equal("i", Assert.IsType<Identifier>(access.Target).Name);

        var indexInit = Assert.IsType<ComputedPropertyTableInitializer>(table.Initializers[1]);
        var key = Assert.IsType<StringLiteral>(indexInit.Key);
        Assert.Equal("b", key.Value);
        var value = Assert.IsType<BooleanLiteral>(indexInit.Value);
        Assert.False(value.Value);
    }

    [Fact]
    public void Generates_WithOperator_ShorthandField()
    {
        var luauTree = Utility.GetLuauAST(
            "interface I { x: number } let i = new I { x: 1 }; let x = 2; let j = i with { x }",
            true
        );

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        var table = Assert.IsType<Table>(variable.Initializer);
        var propInit = Assert.IsType<PropertyTableInitializer>(Assert.Single(table.Initializers));
        Assert.Equal("x", propInit.PropertyName);
        Assert.Equal("x", Assert.IsType<Identifier>(propInit.Value).Name);
    }

    [Fact]
    public void Generates_WithOperator_HoistsNonRepeatableOperand_ToEvaluateItOnce()
    {
        var luauTree = Utility.GetLuauAST(
            "interface I { x: number, y: string } fn get_i(): I -> new I { x: 1, y: 'a' }; let j = get_i() with { x: 2 }",
            true
        );

        var subject = Assert.IsType<ConstVariable>(luauTree.Statements[^2]);
        Assert.Equal("with_subject", subject.Name);
        Assert.IsType<Call>(subject.Initializer);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        var table = Assert.IsType<Table>(variable.Initializer);
        var y = Assert.IsType<PropertyTableInitializer>(table.Initializers[1]);
        var access = Assert.IsType<PropertyAccess>(y.Value);
        Assert.Equal("with_subject", Assert.IsType<Identifier>(access.Target).Name);
    }

    [Fact]
    public void Generates_WithOperator_OnTraitImplementingInterface_PreservesMetatable_ExcludesTraitMethodFromFields()
    {
        var luauTree = Utility.GetLuauAST(
            """
            trait Execute { fn execute(): void; }
            interface I { x: number }
            implement Execute for I { fn execute() -> @; }
            let i = new I { x: 1 };
            let j = i with { x: 2 }
            """,
            true
        );

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        var cast = Assert.IsType<TypeCast>(variable.Initializer);
        var call = Assert.IsType<Call>(cast.Expression);
        var table = Assert.IsType<Table>(call.Arguments[0]);
        Assert.Single(table.Initializers);
    }

    [Fact]
    public void Generates_InterfaceInvocation_ChainedProperty()
    {
        var luauTree = Utility.GetLuauAST("interface I { x: number } let _ = new I { x: 1 }.x", true);
        Assert.True(luauTree.Statements.Count >= 2);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var propAccess = Assert.IsType<PropertyAccess>(variable.Initializer);
        Assert.Equal("x", propAccess.Names[0]);

        var table = Assert.IsType<Table>(propAccess.Target);
        Assert.Single(table.Initializers);
        var propInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        Assert.Equal("x", propInit.PropertyName);
    }

    [Fact]
    public void Generates_IfStatement_SingleExpressionThenBranch()
    {
        var luauTree = Utility.GetLuauAST("if true return 1");
        Assert.Single(luauTree.Statements);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.First());
        Assert.Single(ifStatement.ThenBranch.Statements);
        Assert.IsType<Return>(ifStatement.ThenBranch.Statements[0]);
    }

    [Fact]
    public void Generates_IfStatement_SingleExpressionElseBranch()
    {
        var luauTree = Utility.GetLuauAST("if true { return 1 } else return 0");
        Assert.Single(luauTree.Statements);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.First());
        Assert.NotNull(ifStatement.ElseBranch);
        Assert.Single(ifStatement.ElseBranch.Statements);
        Assert.IsType<Return>(ifStatement.ElseBranch.Statements[0]);
    }

    [Fact]
    public void Generates_Declared_InterfaceDeclaration()
    {
        var luauTree = Utility.GetLuauAST("declare interface I;", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("I", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.Null(tableType.Indexer);
        Assert.Empty(tableType.Properties);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_NoBody()
    {
        var luauTree = Utility.GetLuauAST("interface I;", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("I", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.Null(tableType.Indexer);
        Assert.Empty(tableType.Properties);
    }

    [Fact]
    public void Generates_CompoundAssignment()
    {
        var luauTree = Utility.GetLuauAST("mut x = 1; x += 2");
        Assert.Equal(2, luauTree.Statements.Count);

        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements[1]);
        var binary = Assert.IsType<BinaryOperator>(exprStmt.Expression);
        Assert.Equal("x", ((Identifier)binary.Left).Name);
        Assert.Equal(2, ((NumberLiteral)binary.Right).Value);
        Assert.Equal("+=", binary.Operator);
    }

    [Fact]
    public void Generates_BitwiseAssignment_Nested()
    {
        var luauTree = Utility.GetLuauAST("mut x = 1; x &= 2 | 3");
        Assert.Equal(2, luauTree.Statements.Count);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[1]);
        var binary = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        Assert.Equal("=", binary.Operator);
        Assert.IsType<Identifier>(binary.Left);

        var bandCall = Assert.IsType<Call>(binary.Right);
        var band = Assert.IsType<PropertyAccess>(bandCall.Callee);
        Assert.Equal(2, bandCall.Arguments.Count);
        Assert.Equal("bit32", Assert.IsType<Identifier>(band.Target).Name);
        Assert.Equal("band", Assert.Single(band.Names));
        Assert.Equal("x", Assert.IsType<Identifier>(bandCall.Arguments[0]).Name);

        var borCall = Assert.IsType<Call>(bandCall.Arguments[1]);
        var bor = Assert.IsType<PropertyAccess>(borCall.Callee);
        Assert.Equal(2, borCall.Arguments.Count);
        Assert.Equal("bit32", Assert.IsType<Identifier>(bor.Target).Name);
        Assert.Equal("bor", Assert.Single(bor.Names));
        Assert.Equal(2, Assert.IsType<NumberLiteral>(borCall.Arguments[0]).Value);
        Assert.Equal(3, Assert.IsType<NumberLiteral>(borCall.Arguments[1]).Value);
    }

    [Fact]
    public void Generates_BitwiseAssignment_Flattened()
    {
        var luauTree = Utility.GetLuauAST("mut x = 1; x &= 2 & 3");
        Assert.Equal(2, luauTree.Statements.Count);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[1]);
        var binary = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        Assert.Equal("=", binary.Operator);
        Assert.IsType<Identifier>(binary.Left);

        var bandCall = Assert.IsType<Call>(binary.Right);
        var band = Assert.IsType<PropertyAccess>(bandCall.Callee);
        Assert.Equal(3, bandCall.Arguments.Count);
        Assert.Equal("bit32", Assert.IsType<Identifier>(band.Target).Name);
        Assert.Equal("band", Assert.Single(band.Names));
        Assert.Equal("x", Assert.IsType<Identifier>(bandCall.Arguments[0]).Name);
        Assert.Equal(2, Assert.IsType<NumberLiteral>(bandCall.Arguments[1]).Value);
        Assert.Equal(3, Assert.IsType<NumberLiteral>(bandCall.Arguments[2]).Value);
    }

    [Theory]
    [InlineData("&", "band")]
    [InlineData("|", "bor")]
    [InlineData("~", "bxor")]
    [InlineData(">>", "arshift")]
    [InlineData(">>>", "rshift")]
    [InlineData("<<", "lshift")]
    public void Generates_MappedBitwiseAssignment(string op, string fnName)
    {
        var luauTree = Utility.GetLuauAST($"mut x = 1; x {op}= 2");
        Assert.Equal(2, luauTree.Statements.Count);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[1]);
        var binary = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        Assert.Equal("=", binary.Operator);
        Assert.IsType<Identifier>(binary.Left);

        var bandCall = Assert.IsType<Call>(binary.Right);
        var band = Assert.IsType<PropertyAccess>(bandCall.Callee);
        Assert.Equal(2, bandCall.Arguments.Count);
        Assert.Equal("bit32", Assert.IsType<Identifier>(band.Target).Name);
        Assert.Equal(fnName, Assert.Single(band.Names));
        Assert.Equal("x", Assert.IsType<Identifier>(bandCall.Arguments[0]).Name);
        Assert.Equal(2, Assert.IsType<NumberLiteral>(bandCall.Arguments[1]).Value);
    }

    [Fact]
    public void Generates_ElementAccess_StringIndex()
    {
        var luauTree = Utility.GetLuauAST("interface I { [string]: number } let x = none as never as I; x['key']", true);
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var propertyAccess = Assert.IsType<PropertyAccess>(variable.Initializer);
        var target = Assert.IsType<Identifier>(propertyAccess.Target);
        Assert.Equal("x", target.Name);
        Assert.Equal("key", Assert.Single(propertyAccess.Names));
    }

    [Fact]
    public void Generates_PropertyAccessAssignment()
    {
        var luauTree = Utility.GetLuauAST("interface I { mut prop: number } let obj = none as never as I; obj.prop = 42", true);
        Assert.True(luauTree.Statements.Count >= 3);
        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var assignment = Assert.IsType<BinaryOperator>(exprStmt.Expression);
        var left = Assert.IsType<PropertyAccess>(assignment.Left);
        var right = Assert.IsType<NumberLiteral>(assignment.Right);
        Assert.Equal("obj", ((Identifier)left.Target).Name);
        Assert.Equal("prop", left.Names[0]);
        Assert.Equal(42, right.Value);
    }

    [Fact]
    public void Generates_QualifiedNameAssignment()
    {
        var luauTree = Utility.GetLuauAST("interface Mod { mut value: number } let mod = none as never as Mod; mod.value = 99", true);
        Assert.True(luauTree.Statements.Count >= 3);
        var exprStmt = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var assignment = Assert.IsType<BinaryOperator>(exprStmt.Expression);
        var left = Assert.IsType<PropertyAccess>(assignment.Left);
        var right = Assert.IsType<NumberLiteral>(assignment.Right);
        Assert.Equal("mod", ((Identifier)left.Target).Name);
        Assert.Equal("value", left.Names[0]);
        Assert.Equal(99, right.Value);
    }

    [Fact]
    public void Generates_IdentifierAssignment()
    {
        var luauTree = Utility.GetLuauAST("mut a = 0; let x = a = 1");
        Assert.Equal(3, luauTree.Statements.Count);

        var aVar = Assert.IsType<LocalVariable>(luauTree.Statements[0]);
        Assert.Equal("a", aVar.Name);

        var postreq = Assert.IsType<ExpressionStatement>(luauTree.Statements[1]);
        var assignment = Assert.IsType<BinaryOperator>(postreq.Expression);
        Assert.Equal("a", ((Identifier)assignment.Left).Name);
        var rhs = Assert.IsType<NumberLiteral>(assignment.Right);
        Assert.Equal(1, rhs.Value);

        var xVar = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("x", xVar.Name);
        var init = Assert.IsType<Identifier>(xVar.Initializer);
        Assert.Equal("a", init.Name);
    }

    [Fact]
    public void Generates_FunctionType_WithLiteralReturnType()
    {
        var luauTree = Utility.GetLuauAST("type X = fn(): 0");
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Single());
        var fnType = Assert.IsType<FunctionType>(typeAlias.Type);
        var returnType = Assert.IsType<PrimitiveType>(fnType.ReturnType);
        Assert.Equal(PrimitiveTypeKind.Number, returnType.Kind);
    }

    [Fact]
    public void Generates_Runtime_ImportWhenNecessary()
    {
        var luauTree = Utility.GetLuauAST("let x: Range = 1..10;", disableRuntimeLib: false);
        Assert.Equal(2, luauTree.Statements.Count);

        var importVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.Equal(LuauFactory.RuntimeImportName, importVariable.Name);
        Assert.Null(importVariable.DeclaredType);
        Assert.Equal("require", Assert.IsType<Identifier>(Assert.IsType<Call>(importVariable.Initializer).Callee).Name);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var qualifiedType = Assert.IsType<QualifiedTypeName>(variable.DeclaredType);
        Assert.Equal(LuauFactory.RuntimeImportName, Assert.Single(qualifiedType.Qualifications));
        Assert.Equal("Range", qualifiedType.FinalName.Name);
        Assert.Empty(qualifiedType.FinalName.TypeArguments);
    }

    [Theory]
    [InlineData("Range")]
    [InlineData("Result")]
    [InlineData("ResultOk")]
    [InlineData("ResultError")]
    public void Generates_Qualified_IntrinsicType(string typeName)
    {
        var luauTree = Utility.GetLuauAST($"let x: {typeName};");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        var qualifiedType = Assert.IsType<QualifiedTypeName>(variable.DeclaredType);
        Assert.Equal(LuauFactory.RuntimeImportName, Assert.Single(qualifiedType.Qualifications));
        Assert.Equal(typeName, qualifiedType.FinalName.Name);
        Assert.Empty(qualifiedType.FinalName.TypeArguments);
    }

    [Fact]
    public void Generates_VariableDeclaration_WithoutInitializer()
    {
        var luauTree = Utility.GetLuauAST("let x: number;");
        Assert.Single(luauTree.Statements);
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.Equal("x", variable.Name);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_Empty()
    {
        var luauTree = Utility.GetLuauAST("interface I { }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("I", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.Null(tableType.Indexer);
        Assert.Empty(tableType.Properties);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_WithProperties()
    {
        var luauTree = Utility.GetLuauAST("interface I { x: number, y: string }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.Equal(2, tableType.Properties.Count);

        var propX = tableType.Properties[0];
        Assert.Equal("x", propX.Name);
        Assert.Equal(LuauVisibility.Read, propX.Visibility);
        Assert.IsType<PrimitiveType>(propX.Type);
        Assert.Equal(PrimitiveTypeKind.Number, ((PrimitiveType)propX.Type).Kind);

        var propY = tableType.Properties[1];
        Assert.Equal("y", propY.Name);
        Assert.Equal(LuauVisibility.Read, propY.Visibility);
        Assert.IsType<PrimitiveType>(propY.Type);
        Assert.Equal(PrimitiveTypeKind.String, ((PrimitiveType)propY.Type).Kind);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_WithMutableProperty()
    {
        var luauTree = Utility.GetLuauAST("interface I { mut count: number }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        var prop = tableType.Properties.Single();
        Assert.Equal("count", prop.Name);
        Assert.Null(prop.Visibility);
        Assert.IsType<PrimitiveType>(prop.Type);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_WithIndexer()
    {
        var luauTree = Utility.GetLuauAST("interface I { [number]: string }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.NotNull(tableType.Indexer);

        var keyType = Assert.IsType<PrimitiveType>(tableType.Indexer.KeyType);
        var valueType = Assert.IsType<PrimitiveType>(tableType.Indexer.ValueType);
        Assert.Equal(PrimitiveTypeKind.Number, keyType.Kind);
        Assert.Equal(PrimitiveTypeKind.String, valueType.Kind);
        Assert.Empty(tableType.Properties);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_WithStringIndexer()
    {
        var luauTree = Utility.GetLuauAST("interface I { [string]: bool }", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Single());
        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.NotNull(tableType.Indexer);
        Assert.IsType<PrimitiveType>(tableType.Indexer.ValueType);
        Assert.Equal(PrimitiveTypeKind.Boolean, ((PrimitiveType)tableType.Indexer.ValueType).Kind);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_WithIndexerAndProperties()
    {
        var luauTree = Utility.GetLuauAST("interface I { [number]: string, name: string, mut counter: number }", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Single());
        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        Assert.NotNull(tableType.Indexer);
        Assert.Equal(2, tableType.Properties.Count);
        Assert.Contains(tableType.Properties, p => p is { Name: "name", Visibility: LuauVisibility.Read });
        Assert.Contains(tableType.Properties, p => p is { Name: "counter", Visibility: null });
    }

    [Fact]
    public void Generates_InterfaceDeclaration_WithSingleConstraint()
    {
        var luauTree = Utility.GetLuauAST("interface Base {}; interface I : Base { }", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Last());
        var intersection = Assert.IsType<IntersectionType>(typeAlias.Type);
        Assert.Equal(2, intersection.Types.Count);

        var constraintType = Assert.IsType<TypeName>(intersection.Types[0]);
        Assert.Equal("Base", constraintType.Name);

        var tableType = Assert.IsType<TableType>(intersection.Types[1]);
        Assert.Empty(tableType.Properties);
        Assert.Null(tableType.Indexer);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_WithMultipleConstraints()
    {
        var luauTree = Utility.GetLuauAST("interface A {} interface B {} interface I : A, B { }", true);
        Assert.Equal(3, luauTree.Statements.Count);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Last());
        var intersection = Assert.IsType<IntersectionType>(typeAlias.Type);
        Assert.Equal(3, intersection.Types.Count);
        Assert.Equal("A", ((TypeName)intersection.Types[0]).Name);
        Assert.Equal("B", ((TypeName)intersection.Types[1]).Name);
        Assert.IsType<TableType>(intersection.Types[2]);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_Generic()
    {
        var luauTree = Utility.GetLuauAST("interface Container<T> { value: T }", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Single());
        Assert.Single(typeAlias.TypeParameters.Parameters);
        Assert.Equal("T", typeAlias.TypeParameters.Parameters[0].Name);
        Assert.False(typeAlias.TypeParameters.Parameters[0].OfFunction);

        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        var prop = tableType.Properties.Single();
        Assert.Equal("value", prop.Name);
        var propType = Assert.IsType<TypeName>(prop.Type);
        Assert.Equal("T", propType.Name);
    }

    [Fact]
    public void Generates_InterfaceDeclaration_GenericWithConstraintAndDefault()
    {
        var luauTree = Utility.GetLuauAST("interface Repo<T: number = 42> { item: T }", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.Single());
        Assert.Single(typeAlias.TypeParameters.Parameters);

        var tp = typeAlias.TypeParameters.Parameters.First();
        Assert.Equal("T", tp.Name);
        Assert.NotNull(tp.DefaultType);
        Assert.IsType<PrimitiveType>(tp.DefaultType);
        Assert.Equal(PrimitiveTypeKind.Number, ((PrimitiveType)tp.DefaultType).Kind);

        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        var prop = tableType.Properties.Single();
        Assert.Equal("item", prop.Name);

        var intersection = Assert.IsType<IntersectionType>(prop.Type);
        Assert.Equal(2, intersection.Types.Count);
        Assert.IsType<TypeName>(intersection.Types.First());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("void")]
    public void Generates_FunctionType_WithPrimitiveReturn_ThatUsesUnitConversion(string returnType)
    {
        var luauTree = Utility.GetLuauAST($"type Callback = fn(): {returnType}");
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        var functionType = Assert.IsType<FunctionType>(typeAlias.Type);
        Assert.Empty(functionType.ParameterTypes);
        Assert.IsType<UnitType>(functionType.ReturnType);

        var rendered = functionType.Render();
        Assert.Contains("()", rendered);
    }

    [Fact]
    public void Generates_FunctionType()
    {
        var luauTree = Utility.GetLuauAST("type Optional = fn(x: number, y: string?): bool");
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        var functionType = Assert.IsType<FunctionType>(typeAlias.Type);
        Assert.Equal(2, functionType.ParameterTypes.Count);
        Assert.IsType<PrimitiveType>(functionType.ParameterTypes.First());
        Assert.IsType<OptionalType>(functionType.ParameterTypes.Last());
        Assert.IsType<PrimitiveType>(functionType.ReturnType);
    }

    [Fact]
    public void Generates_IndexedType()
    {
        var luauTree = Utility.GetLuauAST("type Foo = number[]; type X = Foo[number]");
        Assert.Equal(2, luauTree.Statements.Count);

        var alias = Assert.IsType<TypeAlias>(luauTree.Statements.Last());
        Assert.Empty(alias.TypeParameters.Parameters);

        var indexTypeFn = Assert.IsType<TypeName>(alias.Type);
        Assert.Equal("index", indexTypeFn.Name);
        Assert.Equal(2, indexTypeFn.TypeArguments.Count);

        var self = Assert.IsType<TypeName>(indexTypeFn.TypeArguments.First());
        Assert.Equal("Foo", self.Name);
        Assert.Empty(self.TypeArguments);

        var inner = Assert.IsType<PrimitiveType>(indexTypeFn.TypeArguments.Last());
        Assert.Equal(PrimitiveTypeKind.Number, inner.Kind);
    }

    [Fact]
    public void Generates_Array_TableType()
    {
        var luauTree = Utility.GetLuauAST("mut x: number[];");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var table = Assert.IsType<TableType>(variable.DeclaredType);
        Assert.NotNull(table.Indexer);
        Assert.Null(table.Indexer.KeyType);

        var inner = Assert.IsType<PrimitiveType>(table.Indexer.ValueType);
        Assert.Equal(PrimitiveTypeKind.Number, inner.Kind);
    }

    [Fact]
    public void Generates_IntersectionType()
    {
        var luauTree = Utility.GetLuauAST("mut x: number & bool");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var intersection = Assert.IsType<IntersectionType>(variable.DeclaredType);
        Assert.Equal(2, intersection.Types.Count);

        var left = Assert.IsType<PrimitiveType>(intersection.Types.First());
        var right = Assert.IsType<PrimitiveType>(intersection.Types.Last());
        Assert.Equal(PrimitiveTypeKind.Number, left.Kind);
        Assert.Equal(PrimitiveTypeKind.Boolean, right.Kind);
    }

    [Fact]
    public void Generates_UnionType()
    {
        var luauTree = Utility.GetLuauAST("mut x: number | bool");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var union = Assert.IsType<UnionType>(variable.DeclaredType);
        Assert.Equal(2, union.Types.Count);

        var left = Assert.IsType<PrimitiveType>(union.Types.First());
        var right = Assert.IsType<PrimitiveType>(union.Types.Last());
        Assert.Equal(PrimitiveTypeKind.Number, left.Kind);
        Assert.Equal(PrimitiveTypeKind.Boolean, right.Kind);
    }

    [Fact]
    public void Generates_OptionalType()
    {
        var luauTree = Utility.GetLuauAST("mut x: number?;");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var optional = Assert.IsType<OptionalType>(variable.DeclaredType);
        var inner = Assert.IsType<PrimitiveType>(optional.Inner);
        Assert.Equal(PrimitiveTypeKind.Number, inner.Kind);
    }

    [Fact]
    public void Generates_BooleanLiteralType()
    {
        var luauTree = Utility.GetLuauAST("mut x: true;");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var literalType = Assert.IsType<BooleanLiteralType>(variable.DeclaredType);
        Assert.Equal("true", literalType.Render());
    }

    [Fact]
    public void Generates_StringLiteralType()
    {
        var luauTree = Utility.GetLuauAST("mut x: 'abc';");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var literalType = Assert.IsType<StringLiteralType>(variable.DeclaredType);
        Assert.Equal("\"abc\"", literalType.Render());
    }

    [Fact]
    public void Generates_Unusable_LiteralType()
    {
        var luauTree = Utility.GetLuauAST("mut x: 42069;");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var primitive = Assert.IsType<PrimitiveType>(variable.DeclaredType);
        Assert.Equal("number", primitive.Render());
    }

    [Fact]
    public void Generates_ParenthesizedType()
    {
        var luauTree = Utility.GetLuauAST("mut x: (number);");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var parenthesized = Assert.IsType<ParenthesizedType>(variable.DeclaredType);
        var primitive = Assert.IsType<PrimitiveType>(parenthesized.Type);
        Assert.Equal("number", primitive.Render());
    }

    [Theory]
    [InlineData("number")]
    [InlineData("string")]
    [InlineData("bool", "boolean")]
    [InlineData("never")]
    [InlineData("unknown")]
    [InlineData("none", "nil")]
    [InlineData("void", "nil")]
    public void Generates_PrimitiveTypes(string name, string? expected = null)
    {
        var luauTree = Utility.GetLuauAST($"mut x: {name};");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.NotNull(variable.DeclaredType);

        var primitive = Assert.IsType<PrimitiveType>(variable.DeclaredType);
        Assert.Equal(expected ?? name, primitive.Render());
    }

    [Fact]
    public void Generates_SimpleIfStatement()
    {
        var luauTree = Utility.GetLuauAST("if true { return 1 }");
        Assert.Single(luauTree.Statements);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.First());
        var condition = Assert.IsType<BooleanLiteral>(ifStatement.Condition);
        Assert.True(condition.Value);
        Assert.Single(ifStatement.ThenBranch.Statements);

        var returnStatement = Assert.IsType<Return>(ifStatement.ThenBranch.Statements.First());
        var returnValue = Assert.IsType<NumberLiteral>(returnStatement.Expression);
        Assert.Equal(1, returnValue.Value);
        Assert.Empty(ifStatement.ElseIfBranches);
        Assert.Null(ifStatement.ElseBranch);
    }

    [Fact]
    public void Generates_IfElseStatement()
    {
        var luauTree = Utility.GetLuauAST("if x > 5 { return 1 } else { return 0 }");
        Assert.Single(luauTree.Statements);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.First());
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        var left = Assert.IsType<Identifier>(condition.Left);
        var right = Assert.IsType<NumberLiteral>(condition.Right);
        Assert.Equal("x", left.Name);
        Assert.Equal(5, right.Value);
        Assert.Equal(">", condition.Operator);
        Assert.Single(ifStatement.ThenBranch.Statements);

        var thenReturn = Assert.IsType<Return>(ifStatement.ThenBranch.Statements.First());
        var thenValue = Assert.IsType<NumberLiteral>(thenReturn.Expression);
        Assert.Equal(1, thenValue.Value);
        Assert.NotNull(ifStatement.ElseBranch);
        Assert.Single(ifStatement.ElseBranch.Statements);

        var elseReturn = Assert.IsType<Return>(ifStatement.ElseBranch.Statements.First());
        var elseValue = Assert.IsType<NumberLiteral>(elseReturn.Expression);
        Assert.Equal(0, elseValue.Value);
        Assert.Empty(ifStatement.ElseIfBranches);
    }

    [Fact]
    public void Generates_IfElseIfStatement()
    {
        var luauTree = Utility.GetLuauAST("if x > 5 { return 1 } else if x < 0 { return -1 } else { return 0 }");
        Assert.Single(luauTree.Statements);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.First());
        var mainCondition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        var mainLeft = Assert.IsType<Identifier>(mainCondition.Left);
        var mainRight = Assert.IsType<NumberLiteral>(mainCondition.Right);
        Assert.Equal("x", mainLeft.Name);
        Assert.Equal(5, mainRight.Value);
        Assert.Single(ifStatement.ThenBranch.Statements);
        Assert.Single(ifStatement.ElseIfBranches);

        var elseIf = ifStatement.ElseIfBranches.First();
        var elseIfCondition = Assert.IsType<BinaryOperator>(elseIf.Condition);
        var elseIfLeft = Assert.IsType<Identifier>(elseIfCondition.Left);
        var elseIfRight = Assert.IsType<NumberLiteral>(elseIfCondition.Right);
        Assert.Equal("x", elseIfLeft.Name);
        Assert.Equal(0, elseIfRight.Value);
        Assert.Equal("<", elseIfCondition.Operator);
        Assert.Single(elseIf.Branch.Statements);

        var elseIfReturn = Assert.IsType<Return>(elseIf.Branch.Statements.First());
        var elseIfUnary = Assert.IsType<UnaryOperator>(elseIfReturn.Expression);
        var unaryValue = Assert.IsType<NumberLiteral>(elseIfUnary.Operand);
        Assert.Equal("-", elseIfUnary.Operator);
        Assert.Equal(1, unaryValue.Value);
        Assert.NotNull(ifStatement.ElseBranch);
        Assert.Single(ifStatement.ElseBranch.Statements);

        var elseReturn = Assert.IsType<Return>(ifStatement.ElseBranch.Statements.First());
        var elseValue = Assert.IsType<NumberLiteral>(elseReturn.Expression);
        Assert.Equal(0, elseValue.Value);
    }

    [Fact]
    public void Generates_MultipleElseIfBranches()
    {
        var luauTree = Utility.GetLuauAST("if x == 1 { return 1 } else if x == 2 { return 2 } else if x == 3 { return 3 } else { return 0 }");
        Assert.Single(luauTree.Statements);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.First());
        Assert.Equal(2, ifStatement.ElseIfBranches.Count);

        var firstElseIf = ifStatement.ElseIfBranches[0];
        var firstCondition = Assert.IsType<BinaryOperator>(firstElseIf.Condition);
        Assert.Equal("==", firstCondition.Operator);

        var firstValue = Assert.IsType<NumberLiteral>(firstCondition.Right);
        Assert.Equal(2, firstValue.Value);

        var secondElseIf = ifStatement.ElseIfBranches[1];
        var secondCondition = Assert.IsType<BinaryOperator>(secondElseIf.Condition);
        Assert.Equal("==", secondCondition.Operator);

        var secondValue = Assert.IsType<NumberLiteral>(secondCondition.Right);
        Assert.Equal(3, secondValue.Value);
        Assert.NotNull(ifStatement.ElseBranch);
    }

    [Fact]
    public void Generates_IfStatement_WithBlockBody()
    {
        var luauTree = Utility.GetLuauAST("if true { mut x = 1; mut y = 2; }");
        Assert.Single(luauTree.Statements);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.First());
        Assert.Equal(2, ifStatement.ThenBranch.Statements.Count);
        Assert.IsType<LocalVariable>(ifStatement.ThenBranch.Statements[0]);
        Assert.IsType<LocalVariable>(ifStatement.ThenBranch.Statements[1]);
    }

    [Fact]
    public void Generates_NestedIfStatements()
    {
        var luauTree = Utility.GetLuauAST("if x > 0 { if y > 0 { return 1 } }");
        Assert.Single(luauTree.Statements);

        var outerIf = Assert.IsType<IfStatement>(luauTree.Statements.First());
        Assert.Single(outerIf.ThenBranch.Statements);

        var innerIf = Assert.IsType<IfStatement>(outerIf.ThenBranch.Statements.First());
        var innerCondition = Assert.IsType<BinaryOperator>(innerIf.Condition);
        var innerLeft = Assert.IsType<Identifier>(innerCondition.Left);
        Assert.Equal("y", innerLeft.Name);
        Assert.Single(innerIf.ThenBranch.Statements);
        Assert.IsType<Return>(innerIf.ThenBranch.Statements.First());
    }

    [Fact]
    public void Generates_EnumDeclaration_AsNumberTypeAlias()
    {
        var luauTree = Utility.GetLuauAST("enum Abc { A, B, C }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Abc", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var primitive = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
        Assert.Equal("number", primitive.Render());
    }

    [Fact]
    public void Generates_EnumDeclaration_WithExplicitNumberValues_AsNumberTypeAlias()
    {
        var luauTree = Utility.GetLuauAST("enum Status { Active = 1, Inactive = 0, Pending = 2 }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Status", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var primitive = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
        Assert.Equal("number", primitive.Render());
    }

    [Fact]
    public void Generates_EnumDeclaration_WithStringValues_AsUnionOfStringLiterals()
    {
        var luauTree = Utility.GetLuauAST("enum Colors : string { Red = \"FF0000\", Green = \"00FF00\", Blue = \"0000FF\" }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Colors", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var union = Assert.IsType<UnionType>(typeAlias.Type);
        Assert.Equal(3, union.Types.Count);

        var red = Assert.IsType<StringLiteralType>(union.Types[0]);
        var green = Assert.IsType<StringLiteralType>(union.Types[1]);
        var blue = Assert.IsType<StringLiteralType>(union.Types[2]);
        Assert.Equal("\"FF0000\"", red.Render());
        Assert.Equal("\"00FF00\"", green.Render());
        Assert.Equal("\"0000FF\"", blue.Render());
        Assert.Equal("\"FF0000\" | \"00FF00\" | \"0000FF\"", union.Render());
    }

    [Fact]
    public void Generates_EnumDeclaration_WithMixedValues_AsNumberTypeAlias()
    {
        var luauTree = Utility.GetLuauAST("enum Mixed { A, B = 69, C }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Mixed", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var primitive = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
        Assert.Equal("number", primitive.Render());
    }

    [Fact]
    public void Generates_EnumDeclaration_WithDuplicateStringValues_AsUnionWithDuplicatesRemoved()
    {
        var luauTree = Utility.GetLuauAST("enum Duplicates : string { A = \"same\", B = \"same\", C = \"different\" }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Duplicates", typeAlias.Name);

        var union = Assert.IsType<UnionType>(typeAlias.Type);
        Assert.Equal(2, union.Types.Count);

        var literalTypes = union.Types.Cast<StringLiteralType>().ToList();
        Assert.Contains(literalTypes, t => t.Render() == "\"same\"");
        Assert.Contains(literalTypes, t => t.Render() == "\"different\"");
        Assert.Equal("\"same\" | \"different\"", union.Render());
    }

    [Fact]
    public void Generates_EmptyEnum_AsNumberTypeAlias()
    {
        var luauTree = Utility.GetLuauAST("enum Empty { }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Empty", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var never = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal(PrimitiveTypeKind.Number, never.Kind);
    }

    [Fact]
    public void Generates_EnumDeclaration_WithSingleStringValue_AsStringLiteralType()
    {
        var luauTree = Utility.GetLuauAST("enum Single : string { Only = \"value\" }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Single", typeAlias.Name);

        var literalType = Assert.IsType<StringLiteralType>(typeAlias.Type);
        Assert.Equal("\"value\"", literalType.Render());
    }

    [Fact]
    public void Generates_EnumDeclaration_WithNumberBaseTypeExplicit_AsNumberTypeAlias()
    {
        var luauTree = Utility.GetLuauAST("enum Values : number { One = 1, Two = 2, Three = 3 }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Values", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var primitive = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Generates_EnumDeclaration_WithNumberBaseTypeImplicit_AsNumberTypeAlias()
    {
        var luauTree = Utility.GetLuauAST("enum Values : number { A, B, C }", true);
        Assert.Single(luauTree.Statements);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Values", typeAlias.Name);
        Assert.Empty(typeAlias.TypeParameters.Parameters);

        var primitive = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Generates_EnumAccess_AsLiteralValue()
    {
        var luauTree = Utility.GetLuauAST("enum Abc { A, B, C }; let x = Abc::A", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements[0]);
        Assert.Equal("Abc", typeAlias.Name);
        var primitive = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal("number", primitive.Render());

        var x = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        Assert.Equal("x", x.Name);
        var literal = Assert.IsType<NumberLiteral>(x.Initializer);
        Assert.Equal(0, literal.Value);
    }

    [Fact]
    public void Generates_EnumIndexedType_OfNumberEnum_AsNumber()
    {
        var luauTree = Utility.GetLuauAST("enum Abc { A, B }; type A = Abc[\"A\"]", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements[1]);
        Assert.Equal("A", typeAlias.Name);

        var primitive = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal("number", primitive.Render());
    }

    [Fact]
    public void Generates_EnumIndexedType_OfStringEnum_AsStringLiteral()
    {
        var luauTree = Utility.GetLuauAST("enum Names : string { X = \"ex\", Y = \"why\" }; type X = Names[\"X\"]", true);
        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements[1]);
        Assert.Equal("X", typeAlias.Name);

        var literal = Assert.IsType<StringLiteralType>(typeAlias.Type);
        Assert.Equal("\"ex\"", literal.Render());
    }

    [Fact]
    public void Generates_EnumIndexedType_InIndexerKey_AsNumber()
    {
        var luauTree = Utility.GetLuauAST(
            """
            enum Message { ShootGun }
            interface ShootGunPacket { velocity: u8 }
            declare interface MessageData { [Message["ShootGun"]]: ShootGunPacket; }
            """,
            true
        );

        Assert.DoesNotContain("index<", luauTree.Render());
        Assert.Contains("[number]: ShootGunPacket", luauTree.Render());
    }

    [Fact]
    public void Generates_NonEnumIndexedType_AsIndexOperator()
    {
        var luauTree = Utility.GetLuauAST("interface Foo { bar: string }; type B = Foo[\"bar\"]", true);
        Assert.Contains("index<Foo, \"bar\">", luauTree.Render());
    }

    [Fact]
    public void Generates_EnumInVariableTypeAnnotation()
    {
        var luauTree = Utility.GetLuauAST("enum Status { Active, Inactive }; let x: Status = Status::Active", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements[0]);
        Assert.Equal("Status", typeAlias.Name);
        var primitive = Assert.IsType<PrimitiveType>(typeAlias.Type);
        Assert.Equal("number", primitive.Render());

        var x = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        Assert.Equal("x", x.Name);
        Assert.NotNull(x.DeclaredType);
        var typeName = Assert.IsType<TypeName>(x.DeclaredType);
        Assert.Equal("Status", typeName.Name);
        Assert.Empty(typeName.TypeArguments);

        var literal = Assert.IsType<NumberLiteral>(x.Initializer);
        Assert.Equal(0, literal.Value);
    }

    [Fact]
    public void Generates_TypeAliases_GenericWithDefault()
    {
        var luauTree = Utility.GetLuauAST("type Id<T = number> = T");
        Assert.Single(luauTree.Statements);

        var alias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Id", alias.Name);
        Assert.Single(alias.TypeParameters.Parameters);

        var parameter = alias.TypeParameters.Parameters.First();
        Assert.Equal("T", parameter.Name);

        var primitive = Assert.IsType<PrimitiveType>(parameter.DefaultType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);

        var typeName = Assert.IsType<TypeName>(alias.Type);
        Assert.Equal("T", typeName.Name);
    }

    [Fact]
    public void Generates_TypeAliases_Generic()
    {
        var luauTree = Utility.GetLuauAST("type Id<T> = T");
        Assert.Single(luauTree.Statements);

        var alias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("Id", alias.Name);
        Assert.Single(alias.TypeParameters.Parameters);

        var parameter = alias.TypeParameters.Parameters.First();
        Assert.Equal("T", parameter.Name);
        Assert.Null(parameter.DefaultType);

        var typeName = Assert.IsType<TypeName>(alias.Type);
        Assert.Equal("T", typeName.Name);
    }

    [Fact]
    public void Generates_TypeAliases()
    {
        var luauTree = Utility.GetLuauAST("type A = bool");
        Assert.Single(luauTree.Statements);

        var alias = Assert.IsType<TypeAlias>(luauTree.Statements.First());
        Assert.Equal("A", alias.Name);

        var primitive = Assert.IsType<PrimitiveType>(alias.Type);
        Assert.Equal(PrimitiveTypeKind.Boolean, primitive.Kind);
    }

    [Fact]
    public void Generates_QualifiedName_AsPropertyAccessChain()
    {
        var luauTree = Utility.GetLuauAST("a.b");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var propAccess = Assert.IsType<PropertyAccess>(variable.Initializer);
        var target = Assert.IsType<Identifier>(propAccess.Target);
        Assert.Equal("a", target.Name);
        Assert.Single(propAccess.Names);
        Assert.Equal("b", propAccess.Names[0]);
    }

    [Fact]
    public void Generates_QualifiedName_Chained()
    {
        var luauTree = Utility.GetLuauAST("a.b.c");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var outerAccess = Assert.IsType<PropertyAccess>(variable.Initializer);
        Assert.Equal(2, outerAccess.Names.Count);
        Assert.Equal("b", outerAccess.Names.First());
        Assert.Equal("c", outerAccess.Names.Last());
    }

    [Fact]
    public void Generates_OptionalChain_SingleAccess()
    {
        var luauTree = Utility.GetLuauAST("a?.b");
        Assert.Equal(3, luauTree.Statements.Count);

        var resultLocal = Assert.IsType<LocalVariable>(luauTree.Statements[0]);
        Assert.Equal("_result", resultLocal.Name);
        Assert.Null(resultLocal.Initializer);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[1]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        var conditionTarget = Assert.IsType<Identifier>(condition.Left);
        Assert.Equal("a", conditionTarget.Name);
        Assert.Equal("~=", condition.Operator);
        Assert.IsType<NilLiteral>(condition.Right);

        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        Assert.Equal("_result", Assert.IsType<Identifier>(thenAssignment.Left).Name);
        var thenAccess = Assert.IsType<PropertyAccess>(thenAssignment.Right);
        Assert.Single(thenAccess.Names);
        Assert.Equal("b", thenAccess.Names[0]);

        var wrapper = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("_result", Assert.IsType<Identifier>(wrapper.Initializer).Name);
    }

    [Fact]
    public void Generates_OptionalChain_Nested()
    {
        var luauTree = Utility.GetLuauAST("a?.b?.c");
        Assert.Equal(3, luauTree.Statements.Count);

        var outerIf = Assert.IsType<IfStatement>(luauTree.Statements[1]);
        var outerCondition = Assert.IsType<BinaryOperator>(outerIf.Condition);
        var outerTarget = Assert.IsType<Identifier>(outerCondition.Left);
        Assert.Equal("a", outerTarget.Name);
        
        var cachedLink = Assert.IsType<ConstVariable>(outerIf.ThenBranch.Statements[0]);
        Assert.Equal("_target", cachedLink.Name);
        var cachedLinkAccess = Assert.IsType<PropertyAccess>(cachedLink.Initializer);
        Assert.Single(cachedLinkAccess.Names);
        Assert.Equal("b", cachedLinkAccess.Names[0]);
        Assert.Equal("a", Assert.IsType<Identifier>(cachedLinkAccess.Target).Name);

        var innerIf = Assert.IsType<IfStatement>(outerIf.ThenBranch.Statements[1]);
        var innerCondition = Assert.IsType<BinaryOperator>(innerIf.Condition);
        Assert.Equal("_target", Assert.IsType<Identifier>(innerCondition.Left).Name);

        var innerThenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(innerIf.ThenBranch.Statements[0]).Expression);
        var finalAccess = Assert.IsType<PropertyAccess>(innerThenAssignment.Right);
        Assert.Single(finalAccess.Names);
        Assert.Equal("c", finalAccess.Names[0]);
        Assert.Equal("_target", Assert.IsType<Identifier>(finalAccess.Target).Name);
    }

    [Fact]
    public void Generates_OptionalChain_MixedWithPlainAccess()
    {
        var luauTree = Utility.GetLuauAST("a?.b.c");
        Assert.Equal(3, luauTree.Statements.Count);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[1]);
        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        var thenAccess = Assert.IsType<PropertyAccess>(thenAssignment.Right);
        Assert.Single(thenAccess.Names);
        Assert.Equal("c", thenAccess.Names[0]);

        var thenAccessTarget = Assert.IsType<PropertyAccess>(thenAccess.Target);
        Assert.Single(thenAccessTarget.Names);
        Assert.Equal("b", thenAccessTarget.Names[0]);
    }

    [Fact]
    public void Generates_OptionalChain_Invocation_PlacesCallInsideShortCircuit()
    {
        var luauTree = Utility.GetLuauAST("a?.b()");
        Assert.Equal(3, luauTree.Statements.Count);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[1]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("a", Assert.IsType<Identifier>(condition.Left).Name);
        
        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        var call = Assert.IsType<Call>(thenAssignment.Right);
        Assert.False(call.IsMethod);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Single(callee.Names);
        Assert.Equal("b", callee.Names[0]);
    }

    [Fact]
    public void Generates_OptionalChain_Invocation_UsesMethodCallSyntax_WithLuauNameAndLuauMethod()
    {
        const string source = """
            declare interface Foo {
                [luau_method, luau_name("DoFoo")]
                do_foo: fn: void;
            }
            let a = none as never as Foo?;
            a?.do_foo();
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[^2]);

        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        var call = Assert.IsType<Call>(thenAssignment.Right);
        Assert.True(call.IsMethod);

        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Single(callee.Names);
        Assert.Equal("DoFoo", callee.Names[0]);
    }

    [Fact]
    public void Generates_OptionalChain_Invocation_UsesMethodCallSyntax_ThroughNestedOptionalProperty()
    {
        const string source = """
            declare interface Foo {
                [luau_method, luau_name("DoFoo")]
                do_foo: fn: void;
            }
            declare interface Bar {
                foo: Foo?;
            }
            let bar = none as never as Bar?;
            bar?.foo?.do_foo();
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var outerIf = Assert.IsType<IfStatement>(luauTree.Statements[^2]);

        var cachedLink = Assert.IsType<ConstVariable>(outerIf.ThenBranch.Statements[0]);
        Assert.Equal("_target", cachedLink.Name);
        Assert.Equal(["foo"], Assert.IsType<PropertyAccess>(cachedLink.Initializer).Names);

        var innerIf = Assert.IsType<IfStatement>(outerIf.ThenBranch.Statements[1]);
        var innerThenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(innerIf.ThenBranch.Statements[0]).Expression);
        var call = Assert.IsType<Call>(innerThenAssignment.Right);
        Assert.True(call.IsMethod);

        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Single(callee.Names);
        Assert.Equal("DoFoo", callee.Names[0]);
        Assert.Equal("_target", Assert.IsType<Identifier>(callee.Target).Name);
    }

    [Fact]
    public void Generates_OptionalElementAccess_SingleAccess()
    {
        var luauTree = Utility.GetLuauAST("a?[0]");
        Assert.Equal(3, luauTree.Statements.Count);

        var resultLocal = Assert.IsType<LocalVariable>(luauTree.Statements[0]);
        Assert.Equal("_result", resultLocal.Name);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[1]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("a", Assert.IsType<Identifier>(condition.Left).Name);
        Assert.Equal("~=", condition.Operator);
        Assert.IsType<NilLiteral>(condition.Right);

        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        Assert.Equal("_result", Assert.IsType<Identifier>(thenAssignment.Left).Name);
        var thenAccess = Assert.IsType<ElementAccess>(thenAssignment.Right);
        Assert.Equal("a", Assert.IsType<Identifier>(thenAccess.Target).Name);
        Assert.Equal(0, Assert.IsType<NumberLiteral>(thenAccess.Index).Value);
    }

    [Fact]
    public void Generates_OptionalElementAccess_Nested()
    {
        var luauTree = Utility.GetLuauAST("a?[0]?[1]");
        Assert.Equal(5, luauTree.Statements.Count);

        var firstIf = Assert.IsType<IfStatement>(luauTree.Statements[1]);
        var firstCondition = Assert.IsType<BinaryOperator>(firstIf.Condition);
        Assert.Equal("a", Assert.IsType<Identifier>(firstCondition.Left).Name);

        var firstThenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(firstIf.ThenBranch.Statements[0]).Expression);
        var firstResult = Assert.IsType<Identifier>(firstThenAssignment.Left).Name;
        var firstAccess = Assert.IsType<ElementAccess>(firstThenAssignment.Right);
        Assert.Equal("a", Assert.IsType<Identifier>(firstAccess.Target).Name);
        Assert.Equal(0, Assert.IsType<NumberLiteral>(firstAccess.Index).Value);

        var secondIf = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var secondCondition = Assert.IsType<BinaryOperator>(secondIf.Condition);
        Assert.Equal(firstResult, Assert.IsType<Identifier>(secondCondition.Left).Name);

        var secondThenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(secondIf.ThenBranch.Statements[0]).Expression);
        var secondAccess = Assert.IsType<ElementAccess>(secondThenAssignment.Right);
        Assert.Equal(firstResult, Assert.IsType<Identifier>(secondAccess.Target).Name);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(secondAccess.Index).Value);
    }

    [Fact]
    public void Generates_OptionalElementAccess_Invocation_PlacesCallInsideShortCircuit()
    {
        var luauTree = Utility.GetLuauAST("a?[0]()");
        Assert.Equal(3, luauTree.Statements.Count);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[1]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("a", Assert.IsType<Identifier>(condition.Left).Name);

        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        var call = Assert.IsType<Call>(thenAssignment.Right);
        var callee = Assert.IsType<ElementAccess>(call.Callee);
        Assert.Equal("a", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal(0, Assert.IsType<NumberLiteral>(callee.Index).Value);
    }

    [Fact]
    public void Generates_OptionalElementAccess_ComposesWithOptionalPropertyAccess()
    {
        var luauTree = Utility.GetLuauAST("a?.b?[0]");
        Assert.Equal(5, luauTree.Statements.Count);

        var outerIf = Assert.IsType<IfStatement>(luauTree.Statements[1]);
        var outerCondition = Assert.IsType<BinaryOperator>(outerIf.Condition);
        Assert.Equal("a", Assert.IsType<Identifier>(outerCondition.Left).Name);

        var outerThenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(outerIf.ThenBranch.Statements[0]).Expression);
        var firstResult = Assert.IsType<Identifier>(outerThenAssignment.Left).Name;
        var propAccess = Assert.IsType<PropertyAccess>(outerThenAssignment.Right);
        Assert.Equal(["b"], propAccess.Names);
        
        var innerIf = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var innerCondition = Assert.IsType<BinaryOperator>(innerIf.Condition);
        Assert.Equal(firstResult, Assert.IsType<Identifier>(innerCondition.Left).Name);

        var innerThenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(innerIf.ThenBranch.Statements[0]).Expression);
        var access = Assert.IsType<ElementAccess>(innerThenAssignment.Right);
        Assert.Equal(firstResult, Assert.IsType<Identifier>(access.Target).Name);
    }

    [Theory]
    [InlineData("a ? [0] : [1]")]
    [InlineData("a?[0]:[1]")]
    public void Generates_OptionalElementAccess_DoesNotHijackTernaryWithArrayLiteral(string source)
    {
        // Disambiguation looks past the closing ']' for a ':' rather than checking for whitespace, so
        // this stays a ternary whether or not it's spaced like 'a?[0]:[1]' - only the absence of a
        // trailing ':' after the bracket makes '?[' read as optional element access.
        var luauTree = Utility.GetLuauAST(source);
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var ifExpression = Assert.IsType<IfExpression>(variable.Initializer);
        Assert.IsType<Table>(ifExpression.ThenBranch);
        Assert.IsType<Table>(ifExpression.ElseBranch);
    }

    [Fact]
    public void Generates_PropertyAccess_OnRangeLiteral()
    {
        var luauTree = Utility.GetLuauAST("(1..10).minimum");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var propAccess = Assert.IsType<PropertyAccess>(variable.Initializer);
        Assert.Single(propAccess.Names);
        Assert.Equal("minimum", propAccess.Names[0]);

        var parenthesized = Assert.IsType<Parenthesized>(propAccess.Target);
        var rangeTable = Assert.IsType<Table>(parenthesized.Expression);
        Assert.Equal(2, rangeTable.Initializers.Count);
        var minInit = Assert.IsType<PropertyTableInitializer>(rangeTable.Initializers[0]);
        var maxInit = Assert.IsType<PropertyTableInitializer>(rangeTable.Initializers[1]);
        Assert.Equal("minimum", minInit.PropertyName);
        Assert.Equal("maximum", maxInit.PropertyName);
        Assert.IsType<NumberLiteral>(minInit.Value);
        Assert.IsType<NumberLiteral>(maxInit.Value);
    }

    [Fact]
    public void Generates_PropertyAccess_OnVariable()
    {
        var luauTree = Utility.GetLuauAST("let r = 1..10; r.minimum");
        Assert.Equal(2, luauTree.Statements.Count);

        var rVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.Equal("r", rVariable.Name);
        Assert.IsType<Table>(rVariable.Initializer);

        var accessVariable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var propAccess = Assert.IsType<PropertyAccess>(accessVariable.Initializer);
        Assert.Single(propAccess.Names);
        Assert.Equal("minimum", propAccess.Names[0]);

        var target = Assert.IsType<Identifier>(propAccess.Target);
        Assert.Equal("r", target.Name);
    }

    [Fact]
    public void Generates_ComputedAssignment()
    {
        var luauTree = Utility.GetLuauAST("mut x = 1; mut y = 2; let z = x = y = 69");
        Assert.Equal(5, luauTree.Statements.Count);

        {
            var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[2]);
            var assignment = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
            var target = Assert.IsType<Identifier>(assignment.Left);
            var value = Assert.IsType<NumberLiteral>(assignment.Right);
            Assert.Equal("=", assignment.Operator);
            Assert.Equal("y", target.Name);
            Assert.Equal(69, value.Value);
        }

        {
            var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[3]);
            var assignment = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
            var target = Assert.IsType<Identifier>(assignment.Left);
            var value = Assert.IsType<Identifier>(assignment.Right);
            Assert.Equal("=", assignment.Operator);
            Assert.Equal("x", target.Name);
            Assert.Equal("y", value.Name);
        }

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        Assert.Null(variable.DeclaredType);

        var finalValue = Assert.IsType<Identifier>(variable.Initializer);
        Assert.Equal("x", finalValue.Name);
    }

    [Fact]
    public void Generates_BasicAssignment()
    {
        var luauTree = Utility.GetLuauAST("mut x = 1; x = 69");
        Assert.Equal(2, luauTree.Statements.Count);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var assignment = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        var target = Assert.IsType<Identifier>(assignment.Left);
        var value = Assert.IsType<NumberLiteral>(assignment.Right);
        Assert.Equal("=", assignment.Operator);
        Assert.Equal("x", target.Name);
        Assert.Equal(69, value.Value);
    }

    [Fact]
    public void Generates_ExpressionBody_Functions()
    {
        var luauTree = Utility.GetLuauAST("fn abc -> 69");
        Assert.Single(luauTree.Statements);

        var fn = Assert.IsType<Function>(luauTree.Statements.First());
        Assert.Null(fn.ReturnType);
        Assert.Null(fn.TypeParameters);
        Assert.Empty(fn.Parameters);
        Assert.Single(fn.Body.Statements);
        Assert.Equal("abc", fn.Name);

        var returnStatement = Assert.IsType<Return>(fn.Body.Statements.First());
        var literal = Assert.IsType<NumberLiteral>(returnStatement.Expression);
        Assert.Equal(69, literal.Value);
    }

    [Theory]
    [InlineData("fn id<T: number>(value: T): T -> value", PrimitiveTypeKind.Number)]
    [InlineData("fn id<T>(value: T): T -> value")]
    [InlineData("fn id<T>(value: T): T { return value }")]
    public void Generates_Generic_Functions(string source, PrimitiveTypeKind? expectedConstraintKind = null)
    {
        var luauTree = Utility.GetLuauAST(source);
        Assert.Single(luauTree.Statements);

        var fn = Assert.IsType<Function>(luauTree.Statements.First());
        if (expectedConstraintKind != null)
        {
            var intersection = Assert.IsType<IntersectionType>(fn.ReturnType);
            Assert.Equal(2, intersection.Types.Count);

            var returnType = Assert.IsType<TypeName>(intersection.Types.First());
            var constraintType = Assert.IsType<PrimitiveType>(intersection.Types.Last());
            Assert.Equal("T", returnType.Name);
            Assert.Equal(expectedConstraintKind, constraintType.Kind);
        }
        else
        {
            var returnType = Assert.IsType<TypeName>(fn.ReturnType);
            Assert.Equal("T", returnType.Name);
        }

        Assert.Equal("id", fn.Name);
        Assert.NotNull(fn.TypeParameters);
        Assert.Single(fn.TypeParameters.Parameters);

        var typeParameter = fn.TypeParameters.Parameters.First();
        Assert.Equal("T", typeParameter.Name);
        Assert.True(typeParameter.OfFunction);
        Assert.Null(typeParameter.DefaultType);
        Assert.Single(fn.Parameters);

        var parameter = fn.Parameters.First();
        Assert.Equal("value", parameter.Name);

        if (expectedConstraintKind != null)
        {
            var intersection = Assert.IsType<IntersectionType>(parameter.DeclaredType);
            Assert.Equal(2, intersection.Types.Count);

            var parameterType = Assert.IsType<TypeName>(intersection.Types.First());
            var constraintType = Assert.IsType<PrimitiveType>(intersection.Types.Last());
            Assert.Equal("T", parameterType.Name);
            Assert.Equal(expectedConstraintKind, constraintType.Kind);
        }
        else
        {
            var parameterType = Assert.IsType<TypeName>(parameter.DeclaredType);
            Assert.Equal("T", parameterType.Name);
        }

        Assert.Single(fn.Body.Statements);

        var returnStatement = Assert.IsType<Return>(fn.Body.Statements.First());
        var identifier = Assert.IsType<Identifier>(returnStatement.Expression);
        Assert.Equal("value", identifier.Name);
    }

    [Fact]
    public void Generates_ExpressionBody_WithElementAccessAssignment()
    {
        const string source = """
                    let a = mut [1, 2, 3]
                    fn abc -> a[69] = 420
            """;

        var luauTree = Utility.GetLuauAST(source);
        Assert.Equal(2, luauTree.Statements.Count);

        var function = Assert.IsType<Function>(luauTree.Statements.Last());
        Assert.Equal("abc", function.Name);
        Assert.Empty(function.Parameters);
        Assert.Null(function.ReturnType);

        var body = function.Body;
        Assert.Equal(3, body.Statements.Count);

        var declaration = Assert.IsType<ConstVariable>(body.Statements[0]);
        Assert.Equal("_assigned", declaration.Name);
        Assert.IsType<NumberLiteral>(declaration.Initializer);

        var expressionStatement = Assert.IsType<ExpressionStatement>(body.Statements[1]);
        var assignment = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        Assert.Equal("=", assignment.Operator);

        var leftElementAccess = Assert.IsType<ElementAccess>(assignment.Left);
        var targetIdentifier = Assert.IsType<Identifier>(leftElementAccess.Target);
        Assert.Equal("a", targetIdentifier.Name);
        var index = Assert.IsType<NumberLiteral>(leftElementAccess.Index);
        Assert.Equal(69, index.Value);

        var assignedValue = Assert.IsType<Identifier>(assignment.Right);
        Assert.Equal("_assigned", assignedValue.Name);

        var returnStatement = Assert.IsType<Return>(body.Statements[2]);
        var returnExpression = Assert.IsType<Identifier>(returnStatement.Expression);
        Assert.Equal("_assigned", returnExpression.Name);
    }

    [Fact]
    public void Generates_ExpressionBody_WithIdentifierAssignment()
    {
        const string source = """
                    let a = 1
                    let b = 2
                    fn abc -> a = b
            """;

        var luauTree = Utility.GetLuauAST(source);
        Assert.Equal(3, luauTree.Statements.Count);

        var function = Assert.IsType<Function>(luauTree.Statements.Last());
        Assert.Equal("abc", function.Name);
        Assert.Empty(function.Parameters);
        Assert.Null(function.ReturnType);

        var body = function.Body;
        Assert.Equal(2, body.Statements.Count);

        var expressionStatement = Assert.IsType<ExpressionStatement>(body.Statements[0]);
        var assignment = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        Assert.Equal("=", assignment.Operator);

        var leftIdentifier = Assert.IsType<Identifier>(assignment.Left);
        Assert.Equal("a", leftIdentifier.Name);

        var assignedValue = Assert.IsType<Identifier>(assignment.Right);
        Assert.Equal("b", assignedValue.Name);

        var returnStatement = Assert.IsType<Return>(body.Statements[1]);
        var returnExpression = Assert.IsType<Identifier>(returnStatement.Expression);
        Assert.Equal("a", returnExpression.Name);
    }

    [Fact]
    public void Generates_ExpressionBody_WithPropertyAccessAssignment()
    {
        const string source = """
                    interface I { mut prop: number }
                    let a = none as never as I
                    fn abc -> a.prop = 69
            """;

        var luauTree = Utility.GetLuauAST(source);
        Assert.Equal(3, luauTree.Statements.Count);

        var function = Assert.IsType<Function>(luauTree.Statements.Last());
        Assert.Equal("abc", function.Name);
        Assert.Empty(function.Parameters);
        Assert.Null(function.ReturnType);

        var body = function.Body;
        Assert.Equal(3, body.Statements.Count);

        var declaration = Assert.IsType<ConstVariable>(body.Statements[0]);
        Assert.Equal("_assigned", declaration.Name);
        Assert.IsType<NumberLiteral>(declaration.Initializer);

        var expressionStatement = Assert.IsType<ExpressionStatement>(body.Statements[1]);
        var assignment = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        Assert.Equal("=", assignment.Operator);

        var propertyAccess = Assert.IsType<PropertyAccess>(assignment.Left);
        var targetIdentifier = Assert.IsType<Identifier>(propertyAccess.Target);
        Assert.Equal("a", targetIdentifier.Name);
        Assert.Single(propertyAccess.Names);
        Assert.Equal("prop", propertyAccess.Names[0]);

        var assignedValue = Assert.IsType<Identifier>(assignment.Right);
        Assert.Equal("_assigned", assignedValue.Name);

        var returnStatement = Assert.IsType<Return>(body.Statements[2]);
        var returnExpression = Assert.IsType<Identifier>(returnStatement.Expression);
        Assert.Equal("_assigned", returnExpression.Name);
    }

    [Theory]
    [InlineData("fn abc -> 69")]
    [InlineData("fn abc { return 69 }")]
    public void Generates_Functions(string source)
    {
        var luauTree = Utility.GetLuauAST(source);
        Assert.Single(luauTree.Statements);

        var fn = Assert.IsType<Function>(luauTree.Statements.First());
        Assert.Null(fn.ReturnType);
        Assert.Null(fn.TypeParameters);
        Assert.Empty(fn.Parameters);
        Assert.Single(fn.Body.Statements);
        Assert.Equal("abc", fn.Name);

        var returnStatement = Assert.IsType<Return>(fn.Body.Statements.First());
        var literal = Assert.IsType<NumberLiteral>(returnStatement.Expression);
        Assert.Equal(69, literal.Value);
    }

    [Fact]
    public void Generates_ConstVariables()
    {
        var luauTree = Utility.GetLuauAST("let x = 1;");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        Assert.Null(variable.DeclaredType);
        Assert.Equal("x", variable.Name);

        var literal = Assert.IsType<NumberLiteral>(variable.Initializer);
        Assert.Equal(1, literal.Value);
    }

    [Fact]
    public void Generates_LocalVariables()
    {
        var luauTree = Utility.GetLuauAST("mut x = 1;");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<LocalVariable>(luauTree.Statements.First());
        Assert.Null(variable.DeclaredType);
        Assert.NotNull(variable.Initializer);
        Assert.Equal("x", variable.Name);

        var literal = Assert.IsType<NumberLiteral>(variable.Initializer);
        Assert.Equal(1, literal.Value);
    }

    [Fact]
    public void Generates_ElementAccess_Assignment_Short()
    {
        var luauTree = Utility.GetLuauAST("let x = abc[1] = 69");
        Assert.Equal(2, luauTree.Statements.Count);

        var bindingVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        var bindingValue = Assert.IsType<NumberLiteral>(bindingVariable.Initializer);
        Assert.Equal("x", bindingVariable.Name);
        Assert.Equal(69, bindingValue.Value);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[1]);
        var assignment = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        Assert.Equal("=", assignment.Operator);

        var elementAccess = Assert.IsType<ElementAccess>(assignment.Left);
        var assignmentValue = Assert.IsType<Identifier>(assignment.Right);
        var identifier = Assert.IsType<Identifier>(elementAccess.Target);
        var index = Assert.IsType<NumberLiteral>(elementAccess.Index);
        Assert.Equal("abc", identifier.Name);
        Assert.Equal(1, index.Value);
        Assert.Equal("x", assignmentValue.Name);
    }

    [Fact]
    public void Generates_ElementAccess_Assignment()
    {
        var luauTree = Utility.GetLuauAST("x[abc[1] = 69]");
        Assert.Equal(3, luauTree.Statements.Count);

        var bindingVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        var bindingValue = Assert.IsType<NumberLiteral>(bindingVariable.Initializer);
        Assert.Equal("_assigned", bindingVariable.Name);
        Assert.Equal(69, bindingValue.Value);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[1]);
        var assignment = Assert.IsType<BinaryOperator>(expressionStatement.Expression);
        Assert.Equal("=", assignment.Operator);

        var elementAccess = Assert.IsType<ElementAccess>(assignment.Left);
        var assignmentValue = Assert.IsType<Identifier>(assignment.Right);
        var identifier = Assert.IsType<Identifier>(elementAccess.Target);
        var index = Assert.IsType<NumberLiteral>(elementAccess.Index);
        Assert.Equal("abc", identifier.Name);
        Assert.Equal(1, index.Value);
        Assert.Equal("_assigned", assignmentValue.Name);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("_", variable.Name);

        var value = Assert.IsType<ElementAccess>(variable.Initializer);
        var name = Assert.IsType<Identifier>(value.Target);
        var indexName = Assert.IsType<Identifier>(value.Index);
        Assert.Equal("x", name.Name);
        Assert.Equal("_assigned", indexName.Name);
    }

    [Fact]
    public void Generates_ElementAccess()
    {
        var luauTree = Utility.GetLuauAST("let abc = [1,2,3]; abc[1]", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var elementAccess = Assert.IsType<ElementAccess>(variable.Initializer);
        var identifier = Assert.IsType<Identifier>(elementAccess.Target);
        var index = Assert.IsType<NumberLiteral>(elementAccess.Index);
        Assert.Equal("abc", identifier.Name);
        Assert.Equal(1, index.Value);
    }

    [Fact]
    public void Generates_RangeLiteral()
    {
        var luauTree = Utility.GetLuauAST("1..10");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Equal(2, table.Initializers.Count);

        var minInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        var maxInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[1]);
        Assert.Equal("minimum", minInit.PropertyName);
        Assert.Equal("maximum", maxInit.PropertyName);

        var minValue = Assert.IsType<NumberLiteral>(minInit.Value);
        var maxValue = Assert.IsType<NumberLiteral>(maxInit.Value);
        Assert.Equal(1, minValue.Value);
        Assert.Equal(10, maxValue.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Generates_ArrayLiterals(bool mutable)
    {
        var luauTree = Utility.GetLuauAST($"let _ = {(mutable ? "mut " : "")}[69, 420];");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Equal(2, table.Initializers.Count);
        Assert.All(table.Initializers, i => Assert.IsType<TableInitializer>(i));

        var firstElement = Assert.IsType<NumberLiteral>(table.Initializers.First().Value);
        var lastElement = Assert.IsType<NumberLiteral>(table.Initializers.Last().Value);
        Assert.Equal(69, firstElement.Value);
        Assert.Equal(420, lastElement.Value);
    }

    [Fact]
    public void Generates_Parenthesized()
    {
        var luauTree = Utility.GetLuauAST("(abc)");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var parenthesized = Assert.IsType<Parenthesized>(variable.Initializer);
        var identifier = Assert.IsType<Identifier>(parenthesized.Expression);
        Assert.Equal("abc", identifier.Name);
    }

    [Fact]
    public void Generates_TypeCasts()
    {
        var luauTree = Utility.GetLuauAST("abc as number");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var typeCast = Assert.IsType<TypeCast>(variable.Initializer);
        var identifier = Assert.IsType<Identifier>(typeCast.Expression);
        var primitive = Assert.IsType<PrimitiveType>(typeCast.Type);
        Assert.Equal("abc", identifier.Name);
        Assert.Equal("number", primitive.Render());
    }

    [Fact]
    public void Generates_NullForgiving_ProducesNonNullableTypeCast()
    {
        var luauTree = Utility.GetLuauAST("let nullable: number? = 5; let forgiven = nullable!;", true, false);
        var variable = luauTree.Statements.OfType<ConstVariable>().Single(v => v.Name == "forgiven");
        var typeCast = Assert.IsType<TypeCast>(variable.Initializer);
        var identifier = Assert.IsType<Identifier>(typeCast.Expression);
        Assert.Equal("nullable", identifier.Name);

        var qualifiedType = Assert.IsType<QualifiedTypeName>(typeCast.Type);
        Assert.Equal(["Loom"], qualifiedType.Qualifications);
        Assert.Equal("NonNullable", qualifiedType.FinalName.Name);

        var typeOf = Assert.IsType<TypeOfType>(Assert.Single(qualifiedType.FinalName.TypeArguments));
        var typeOfIdentifier = Assert.IsType<Identifier>(typeOf.Expression);
        Assert.Equal("nullable", typeOfIdentifier.Name);
    }

    [Fact]
    public void Generates_NullForgiving_RequiresRuntimeImport()
    {
        var luauTree = Utility.GetLuauAST("let nullable: number? = 5; let forgiven = nullable!;", true, false);
        Assert.Contains(luauTree.Statements, s => s is ConstVariable { Name: "Loom" });
    }

    [Fact]
    public void Generates_Identifiers()
    {
        var luauTree = Utility.GetLuauAST("abc");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var identifier = Assert.IsType<Identifier>(variable.Initializer);
        Assert.Equal("abc", identifier.Name);
    }

    [Theory]
    [InlineData("a & b & c & d", true)]
    [InlineData("a << b << c << d", false)]
    public void Generates_ConcatenatedBitwiseArguments(string source, bool isConcatenated)
    {
        var luauTree = Utility.GetLuauAST(source);
        Assert.Single(luauTree.Statements);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.First());
        var call = Assert.IsType<Call>(expressionStatement.Expression);
        Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(isConcatenated ? 4 : 2, call.Arguments.Count);
    }

    [Theory]
    [InlineData("&", "band")]
    [InlineData("|", "bor")]
    [InlineData("~", "bxor")]
    [InlineData("<<", "lshift")]
    [InlineData(">>", "arshift")]
    [InlineData(">>>", "rshift")]
    public void Generates_MappedBitwiseOperators(string op, string expectedMethod)
    {
        var luauTree = Utility.GetLuauAST($"a {op} b");
        Assert.Single(luauTree.Statements);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.First());
        var call = Assert.IsType<Call>(expressionStatement.Expression);
        var access = Assert.IsType<PropertyAccess>(call.Callee);
        var bit32Identifier = Assert.IsType<Identifier>(access.Target);
        Assert.Equal("bit32", bit32Identifier.Name);
        Assert.Single(access.Names);

        var name = access.Names.First();
        Assert.Equal(expectedMethod, name);
    }

    [Fact]
    public void Generates_StringConcatenation()
    {
        var luauTree = Utility.GetLuauAST("'abc' + 'def'", true);
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var binary = Assert.IsType<BinaryOperator>(variable.Initializer);
        var left = Assert.IsType<StringLiteral>(binary.Left);
        var right = Assert.IsType<StringLiteral>(binary.Right);
        Assert.Equal("abc", left.Value);
        Assert.Equal("def", right.Value);
        Assert.Equal("..", binary.Operator);
    }

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("^")]
    [InlineData("==")]
    [InlineData("!=", "~=")]
    [InlineData("&&", "and")]
    [InlineData("||", "or")]
    public void Generates_MappedBinaryOperators(string op, string? mappedOp = null)
    {
        var luauTree = Utility.GetLuauAST($"1 {op} 2");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var binary = Assert.IsType<BinaryOperator>(variable.Initializer);
        var left = Assert.IsType<NumberLiteral>(binary.Left);
        var right = Assert.IsType<NumberLiteral>(binary.Right);
        Assert.Equal(1, left.Value);
        Assert.Equal(2, right.Value);
        Assert.Equal(mappedOp ?? op, binary.Operator);
    }

    [Fact]
    public void Generates_NullCoalesce_AsNilCheck_NotOr()
    {
        var luauTree = Utility.GetLuauAST("let flag: bool? = false; let result = flag ?? true;", true);
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var ifExpression = Assert.IsType<IfExpression>(variable.Initializer);

        var condition = Assert.IsType<BinaryOperator>(ifExpression.Condition);
        Assert.Equal("~=", condition.Operator);
        Assert.IsType<NilLiteral>(condition.Right);
        Assert.Equal("flag", Assert.IsType<Identifier>(condition.Left).Name);
        Assert.Equal("flag", Assert.IsType<Identifier>(ifExpression.ThenBranch).Name);
        Assert.True(Assert.IsType<BooleanLiteral>(ifExpression.ElseBranch).Value);
    }

    [Fact]
    public void Generates_NullCoalesce_OnComplexLeftExpression_EvaluatesItOnce()
    {
        var luauTree = Utility.GetLuauAST("fn get(): bool? -> false; let result = get() ?? true;", true);
        var binding = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        Assert.Equal("_coalesce", binding.Name);
        Assert.IsType<Call>(binding.Initializer);

        var result = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        var ifExpression = Assert.IsType<IfExpression>(result.Initializer);
        var condition = Assert.IsType<BinaryOperator>(ifExpression.Condition);
        Assert.Equal("_coalesce", Assert.IsType<Identifier>(condition.Left).Name);
        Assert.Equal("_coalesce", Assert.IsType<Identifier>(ifExpression.ThenBranch).Name);
    }

    [Theory]
    [InlineData("&&=", "and")]
    [InlineData("||=", "or")]
    public void Generates_CompoundLogicalAssignment_DesugarsToPlainAssignment(string op, string mappedOp)
    {
        var luauTree = Utility.GetLuauAST($"mut a: bool = true; a {op} false;");
        var assignment = Assert.IsType<BinaryOperator>(
            Assert.IsType<ExpressionStatement>(luauTree.Statements[1]).Expression
        );
        Assert.Equal("=", assignment.Operator);

        var value = Assert.IsType<BinaryOperator>(assignment.Right);
        Assert.Equal(mappedOp, value.Operator);
        Assert.Equal("a", Assert.IsType<Identifier>(value.Left).Name);
    }

    [Fact]
    public void Generates_CompoundNullCoalesceAssignment_DesugarsToNilCheck()
    {
        var luauTree = Utility.GetLuauAST("mut a: bool? = true; a ??= false;", true);
        var assignment = Assert.IsType<BinaryOperator>(
            Assert.IsType<ExpressionStatement>(luauTree.Statements[1]).Expression
        );
        Assert.Equal("=", assignment.Operator);

        var ifExpression = Assert.IsType<IfExpression>(assignment.Right);
        var condition = Assert.IsType<BinaryOperator>(ifExpression.Condition);
        Assert.Equal("~=", condition.Operator);
        Assert.IsType<NilLiteral>(condition.Right);
    }

    [Fact]
    public void Generates_InOperator_IdentifierKey_AsDirectPropertyAccess()
    {
        var luauTree = Utility.GetLuauAST("interface Foo { bar: string } let foo = new Foo { bar: \"abc\" }; \"bar\" in foo", true);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binary = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.IsType<PropertyAccess>(binary.Left);
        Assert.Equal("foo.bar ~= nil", binary.Render());
    }

    [Fact]
    public void Generates_InOperator_NonIdentifierKey_AsBracketedAccess()
    {
        var luauTree = Utility.GetLuauAST("interface Foo { [string]: string } let foo = new Foo { [\"foo-bar\"]: \"abc\" }; \"foo-bar\" in foo", true);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binary = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.IsType<PropertyAccess>(binary.Left);
        Assert.Equal("foo[\"foo-bar\"] ~= nil", binary.Render());
    }

    [Fact]
    public void Generates_InOperator_KeywordKey_AsBracketedAccess()
    {
        var luauTree = Utility.GetLuauAST("interface Foo { [string]: string } let foo = new Foo { [\"end\"]: \"abc\" }; \"end\" in foo", true);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binary = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.Equal("foo[\"end\"] ~= nil", binary.Render());
    }

    [Fact]
    public void Generates_InOperator_NonLiteralKey_AsElementAccess()
    {
        var luauTree = Utility.GetLuauAST("interface Foo { bar: string } let foo = new Foo { bar: \"abc\" }; let key = \"bar\"; key in foo", true);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binary = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.IsType<ElementAccess>(binary.Left);
    }

    [Fact]
    public void Generates_UnaryOperators()
    {
        var luauTree = Utility.GetLuauAST("!false");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var unary = Assert.IsType<UnaryOperator>(variable.Initializer);
        Assert.IsType<BooleanLiteral>(unary.Operand);
        Assert.Equal("not ", unary.Operator);
    }

    [Fact]
    public void Generates_NameOf_ForType()
    {
        var luauTree = Utility.GetLuauAST("type T = 69; nameof::<T>()", true);
        Assert.Equal(2, luauTree.Statements.Count);

        Assert.IsType<TypeAlias>(luauTree.Statements.First());
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var literal = Assert.IsType<StringLiteral>(variable.Initializer);
        Assert.Equal("T", literal.Value);
    }

    [Fact]
    public void Generates_NameOf()
    {
        var luauTree = Utility.GetLuauAST("let x = 1; nameof(x)", true);
        Assert.Equal(2, luauTree.Statements.Count);

        Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var literal = Assert.IsType<StringLiteral>(variable.Initializer);
        Assert.Equal("x", literal.Value);
    }

    [Theory]
    [InlineData("420", 420)]
    [InlineData("69.420", 69.42)]
    [InlineData(".5", 0.5)]
    public void Generates_NumberLiterals(string source, double expected)
    {
        var luauTree = Utility.GetLuauAST(source);
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var literal = Assert.IsType<NumberLiteral>(variable.Initializer);
        Assert.Equal(expected, literal.Value);
    }

    [Theory]
    [InlineData("'abc'", "abc")]
    [InlineData("\"def\"", "def")]
    public void Generates_StringLiterals(string source, string expected)
    {
        var luauTree = Utility.GetLuauAST(source);
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var literal = Assert.IsType<StringLiteral>(variable.Initializer);
        Assert.Equal(expected, literal.Value);
    }

    [Fact]
    public void Generates_InterpolatedStringLiterals()
    {
        var luauTree = Utility.GetLuauAST("""let name = "world"; $"Welcome, {name}!" """, true);
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var interpolated = Assert.IsType<InterpolatedString>(variable.Initializer);
        Assert.Equal(3, interpolated.Segments.Count);

        var leading = Assert.IsType<InterpolatedStringTextSegment>(interpolated.Segments[0]);
        Assert.Equal("Welcome, ", leading.Value);

        var hole = Assert.IsType<InterpolatedStringExpressionSegment>(interpolated.Segments[1]);
        Assert.IsType<Identifier>(hole.Expression);

        var trailing = Assert.IsType<InterpolatedStringTextSegment>(interpolated.Segments[2]);
        Assert.Equal("!", trailing.Value);
    }

    [Fact]
    public void Generates_InterpolatedStringLiterals_WithBinaryExpressionHole_Parenthesized()
    {
        var luauTree = Utility.GetLuauAST("""let n = 1; $"{n + 1}" """, true);
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var interpolated = Assert.IsType<InterpolatedString>(variable.Initializer);
        Assert.Equal("`{(n + 1)}`", interpolated.Render());
    }

    [Fact]
    public void Generates_BoolLiterals()
    {
        var luauTree = Utility.GetLuauAST("true");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var literal = Assert.IsType<BooleanLiteral>(variable.Initializer);
        Assert.True(literal.Value);
    }

    [Fact]
    public void Generates_NilLiterals()
    {
        var luauTree = Utility.GetLuauAST("none");
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        Assert.IsType<NilLiteral>(variable.Initializer);
    }

    [Fact]
    public void Generates_EventDisconnect_UsingUserNamedConnection()
    {
        const string source = """
            event abc;
            fn handler(): void { }
            let my_conn = abc += handler;
            abc -= handler;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(4, luauTree.Statements.Count);

        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("my_conn", connVariable.Name);

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[3]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        Assert.True(disconnectCall.IsMethod);
        Assert.Empty(disconnectCall.Arguments);

        var access = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(access.Names));
        Assert.Equal("my_conn", Assert.IsType<Identifier>(access.Target).Name);
    }

    [Fact]
    public void Generates_EventConnect_AutoBindsBareConnection_ForLaterDisconnect()
    {
        const string source = """
            event abc;
            fn handler(): void { }
            abc += handler;
            abc -= handler;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(4, luauTree.Statements.Count);

        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("handler_conn", connVariable.Name);

        var connectCall = Assert.IsType<Call>(connVariable.Initializer);
        Assert.True(connectCall.IsMethod);
        var connectAccess = Assert.IsType<PropertyAccess>(connectCall.Callee);
        Assert.Equal("Connect", Assert.Single(connectAccess.Names));

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[3]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(disconnectAccess.Names));
        Assert.Equal("handler_conn", Assert.IsType<Identifier>(disconnectAccess.Target).Name);
    }

    [Fact]
    public void Generates_EventOnce_AutoBindsBareConnection_ForLaterDisconnect()
    {
        const string source = """
            event abc;
            fn handler(): void { }
            abc ^= handler;
            abc -= handler;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(4, luauTree.Statements.Count);

        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("handler_conn", connVariable.Name);

        var connectCall = Assert.IsType<Call>(connVariable.Initializer);
        Assert.True(connectCall.IsMethod);
        var connectAccess = Assert.IsType<PropertyAccess>(connectCall.Callee);
        Assert.Equal("Once", Assert.Single(connectAccess.Names));

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[3]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(disconnectAccess.Names));
        Assert.Equal("handler_conn", Assert.IsType<Identifier>(disconnectAccess.Target).Name);
    }

    [Fact]
    public void Generates_EventConnect_AcrossFunctionScopes_UsesModuleLevelConnectionStore()
    {
        // Regression test: the connection must be reachable from '-=' even though it's produced
        // inside a different Luau function scope than where it's connected and disconnected.
        const string source = """
            event abc;
            fn h(): void { }
            fn a -> abc += h;
            a();
            abc -= h;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(6, luauTree.Statements.Count);

        var store = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.Equal("_abc_connections", store.Name);

        var fn = Assert.IsType<Function>(luauTree.Statements[3]);
        var connectStatement = Assert.IsType<ExpressionStatement>(fn.Body.Statements[0]);
        var connectAssign = Assert.IsType<BinaryOperator>(connectStatement.Expression);
        var connectionSlot = Assert.IsType<ElementAccess>(connectAssign.Left);
        Assert.Equal("_abc_connections", Assert.IsType<Identifier>(connectionSlot.Target).Name);
        Assert.Equal("h", Assert.IsType<Identifier>(connectionSlot.Index).Name);

        var returnStatement = Assert.IsType<Return>(fn.Body.Statements[1]);
        var returnedSlot = Assert.IsType<ElementAccess>(returnStatement.Expression);
        Assert.Equal("_abc_connections", Assert.IsType<Identifier>(returnedSlot.Target).Name);

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[5]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(disconnectAccess.Names));
        var disconnectSlot = Assert.IsType<ElementAccess>(disconnectAccess.Target);
        Assert.Equal("_abc_connections", Assert.IsType<Identifier>(disconnectSlot.Target).Name);
        Assert.Equal("h", Assert.IsType<Identifier>(disconnectSlot.Index).Name);
    }

    [Fact]
    public void Generates_EventConnect_DisconnectAfterIfBlock_UsesConnectionStore()
    {
        // The connect's local would be declared inside the if's 'then...end' block in Luau, so it's
        // out of scope by the time the disconnect after the block runs - this must use the store.
        const string source = """
            event abc;
            fn h(): void { }
            if true {
                abc += h;
            }
            abc -= h;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(5, luauTree.Statements.Count);

        var store = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.Equal("_abc_connections", store.Name);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var connectStatement = Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements.Single());
        var connectAssign = Assert.IsType<BinaryOperator>(connectStatement.Expression);
        var connectionSlot = Assert.IsType<ElementAccess>(connectAssign.Left);
        Assert.Equal("_abc_connections", Assert.IsType<Identifier>(connectionSlot.Target).Name);

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[4]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(disconnectAccess.Names));
        var disconnectSlot = Assert.IsType<ElementAccess>(disconnectAccess.Target);
        Assert.Equal("_abc_connections", Assert.IsType<Identifier>(disconnectSlot.Target).Name);
    }

    [Fact]
    public void Generates_EventConnect_ConnectBeforeIf_DisconnectInsideIf_UsesLocal()
    {
        // The if's 'then...end' block is nested inside the connect's scope, so the connect's local
        // is visible there as an upvalue - a plain local is safe and correct here.
        const string source = """
            event abc;
            fn h(): void { }
            abc += h;
            if true {
                abc -= h;
            }
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(4, luauTree.Statements.Count);

        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("h_conn", connVariable.Name);
        Assert.IsType<Call>(connVariable.Initializer);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var disconnectStatement = Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements.Single());
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(disconnectAccess.Names));
        Assert.Equal("h_conn", Assert.IsType<Identifier>(disconnectAccess.Target).Name);
    }

    [Fact]
    public void Generates_ExportedEventConnect_ThroughAStore_AndDisconnectThroughTheRuntime()
    {
        // an exported event's connections are shared with every module that imports it, so they go in
        // the store that travels with it rather than in a local only this file can see
        const string source = """
            export event abc;
            fn handler(): void { }
            abc += handler;
            abc -= handler;
            """;

        var luauTree = Utility.GetLuauAST(source, true);

        var store = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.Equal("_abc_connections", store.Name);
        Assert.Empty(Assert.IsType<Table>(store.Initializer).Initializers);

        var connectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[3]);
        var connectAssign = Assert.IsType<BinaryOperator>(connectStatement.Expression);
        var connectionSlot = Assert.IsType<ElementAccess>(connectAssign.Left);
        Assert.Equal("_abc_connections", Assert.IsType<Identifier>(connectionSlot.Target).Name);
        Assert.Equal("handler", Assert.IsType<Identifier>(connectionSlot.Index).Name);

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[4]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectCallee = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("disconnect_event", Assert.Single(disconnectCallee.Names));
        Assert.Equal("Loom", Assert.IsType<Identifier>(disconnectCallee.Target).Name);
        Assert.Equal(["_abc_connections", "handler"], disconnectCall.Arguments.Select(argument => Assert.IsType<Identifier>(argument).Name));

        var exports = Assert.IsType<Return>(luauTree.Statements[5]);
        var exportTable = Assert.IsType<Table>(exports.Expression);
        Assert.Equal(
            ["abc", "_abc_connections"],
            exportTable.Initializers.Select(initializer => Assert.IsType<PropertyTableInitializer>(initializer).PropertyName)
        );
    }

    [Fact]
    public void Generates_EventConnect_ThroughNamespaceImport_UsesTheModulesConnectionStore()
    {
        const string eventsModule = "export event tick;";
        const string mainModule = """
            import * as ev from "./events"
            fn h(): void { }
            ev::tick += h;
            ev::tick -= h;
            """;

        Utility.WithTempProject(
            [("main.loom", mainModule), ("events.loom", eventsModule)],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
                Assert.Contains("_tick_connections[h] = ev.tick:Connect(h)", main.RenderedLuau);
                Assert.Contains("Loom.disconnect_event(_tick_connections, h)", main.RenderedLuau);
            }
        );
    }

    [Theory]
    [InlineData("while true {\n    abc -= h;\n}")]
    [InlineData("for i : 1..3 {\n    abc -= h;\n}")]
    [InlineData("after 1s {\n    abc -= h;\n}")]
    [InlineData("every 1s {\n    abc -= h;\n}")]
    [InlineData("if false {\n} else {\n    abc -= h;\n}")]
    public void Generates_EventConnect_ConnectBeforeNestedScope_DisconnectInside_UsesLocal(string nestedDisconnect)
    {
        var source = $$"""
            event abc;
            fn h(): void { }
            abc += h;
            {{nestedDisconnect}}
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("h_conn", connVariable.Name);
        Assert.IsType<Call>(connVariable.Initializer);
    }

    [Fact]
    public void Generates_EventConnect_ThroughFunctionExpressionClosure_UsesLocal()
    {
        const string source = """
            event abc;
            fn h(): void { }
            abc += h;
            let f = fn(): void { abc -= h; };
            f();
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("h_conn", connVariable.Name);

        var closure = Assert.IsType<ConstVariable>(luauTree.Statements[3]);
        var anonymousFunction = Assert.IsType<AnonymousFunction>(closure.Initializer);
        var disconnectStatement = Assert.IsType<ExpressionStatement>(Assert.Single(anonymousFunction.Body.Statements));
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("h_conn", Assert.IsType<Identifier>(disconnectAccess.Target).Name);
    }

    [Fact]
    public void Generates_EventConnect_AnonymousHandlerWithUntypedParameter_ConnectsDirectly()
    {
        const string source = """
            event abc(x: number);
            abc += fn(x) { print(x) };
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var connectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var connectCall = Assert.IsType<Call>(connectStatement.Expression);
        Assert.True(connectCall.IsMethod);

        var callee = Assert.IsType<PropertyAccess>(connectCall.Callee);
        Assert.Equal("Connect", Assert.Single(callee.Names));

        var handler = Assert.IsType<AnonymousFunction>(Assert.Single(connectCall.Arguments));
        var parameter = Assert.Single(handler.Parameters);
        Assert.Equal("x", parameter.Name);
        Assert.Null(parameter.DeclaredType);

        var printStatement = Assert.IsType<ExpressionStatement>(Assert.Single(handler.Body.Statements));
        var printCall = Assert.IsType<Call>(printStatement.Expression);
        Assert.Equal("x", Assert.IsType<Identifier>(Assert.Single(printCall.Arguments)).Name);
    }

    [Fact]
    public void Generates_EventConnect_OnCallExpressionReceiver_UsesConnectionStore()
    {
        const string source = """
            declare fn get_part(): Part;
            fn on_touch(hit: never): void { }
            get_part().touched += on_touch;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var store = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.Equal("_touched_connections", store.Name);

        var connectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var connectAssign = Assert.IsType<BinaryOperator>(connectStatement.Expression);
        var connectionSlot = Assert.IsType<ElementAccess>(connectAssign.Left);
        Assert.Equal("_touched_connections", Assert.IsType<Identifier>(connectionSlot.Target).Name);
    }

    [Fact]
    public void ThrowsFor_EventDisconnect_WhenFunctionRequiresAnonymousWrapper()
    {
        const string source = """
            trait Execute {
                fn execute(p: number): void;
            }

            interface Foo;

            implement Execute for Foo {
                fn execute(p) -> print(p);
            }

            event my_event(param: number);
            let foo = new Foo {};
            my_event -= foo.execute;
            """;

        var diagnostics = Utility.GetGeneratorDiagnostics(source, true);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.AnonymousEventDisconnect,
            "Cannot disconnect a function reference that gets wrapped into a new Luau closure on every connection.",
            "store the connection returned from '+=' or '^=' and disconnect that instead."
        );
    }

    [Fact]
    public void ThrowsFor_EventDisconnect_WhenNoConnectionWasTracked()
    {
        const string source = """
            event abc;
            fn handler(): void { }
            abc -= handler;
            """;

        var diagnostics = Utility.GetGeneratorDiagnostics(source, true);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.UnresolvedEventDisconnect,
            "No event connection exists for this function, connect it with '+=' or '^=' before disconnecting it."
        );
    }

    [Fact]
    public void ThrowsFor_LuauNameAttribute_WithNonStringLiteralArgument()
    {
        const string source = """
            interface Foo {
                [luau_name(42)]
                bar: number;
            }

            let foo = new Foo { bar: 1 };
            """;

        var diagnostics = Utility.GetGeneratorDiagnostics(source, true);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidLuauNameAttribute,
            "May only use string literals for name parameter on 'luau_name' attribute"
        );
    }

    [Fact]
    public void Generates_EventDisconnect_UsingUserNamedConnection_ForInterfaceMemberEvent()
    {
        const string source = """
            interface Foo {
                event abc;
            }
            fn handler(): void { }
            let eo = none as never as Foo;
            let my_conn = eo.abc += handler;
            eo.abc -= handler;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(5, luauTree.Statements.Count);

        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[3]);
        Assert.Equal("my_conn", connVariable.Name);

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[4]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        Assert.True(disconnectCall.IsMethod);
        Assert.Empty(disconnectCall.Arguments);

        var access = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(access.Names));
        Assert.Equal("my_conn", Assert.IsType<Identifier>(access.Target).Name);
    }

    [Fact]
    public void Generates_EventConnect_AutoBindsBareConnection_ForLaterDisconnect_ForInterfaceMemberEvent()
    {
        const string source = """
            interface Foo {
                event abc;
            }
            fn handler(): void { }
            let eo = none as never as Foo;
            eo.abc += handler;
            eo.abc -= handler;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Assert.Equal(5, luauTree.Statements.Count);

        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[3]);
        Assert.Equal("handler_conn", connVariable.Name);

        var connectCall = Assert.IsType<Call>(connVariable.Initializer);
        Assert.True(connectCall.IsMethod);
        var connectAccess = Assert.IsType<PropertyAccess>(connectCall.Callee);
        Assert.Equal("Connect", Assert.Single(connectAccess.Names));

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[4]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(disconnectAccess.Names));
        Assert.Equal("handler_conn", Assert.IsType<Identifier>(disconnectAccess.Target).Name);
    }

    [Fact]
    public void Generates_DistinctEventConnections_ForDifferentVariablesOfSameInterfaceType()
    {
        const string source = """
            interface Foo {
                event abc;
            }
            fn handler(): void { }
            let eo1 = none as never as Foo;
            let eo2 = none as never as Foo;
            eo1.abc += handler;
            eo2.abc += handler;
            eo1.abc -= handler;
            """;

        var luauTree = Utility.GetLuauAST(source, true);

        Assert.Equal(7, luauTree.Statements.Count);
        var conn1Variable = Assert.IsType<ConstVariable>(luauTree.Statements[4]);
        var conn2Variable = Assert.IsType<ConstVariable>(luauTree.Statements[5]);
        Assert.NotEqual(conn1Variable.Name, conn2Variable.Name);

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[6]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(disconnectAccess.Names));
        Assert.Equal(conn1Variable.Name, Assert.IsType<Identifier>(disconnectAccess.Target).Name);
    }

    [Fact]
    public void Generates_EventConnect_ForInterfaceMemberEvent_AccessedInsideNarrowedIf()
    {
        const string source = """
            interface Foo {
                event abc;
            }
            fn handler(): void { }
            let eo: Foo? = none as never as Foo;
            if eo != none {
                eo.abc += handler;
            }
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.OfType<IfStatement>().Single());
        var connVariable = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements.Single());

        var connectCall = Assert.IsType<Call>(connVariable.Initializer);
        Assert.True(connectCall.IsMethod);
        var connectAccess = Assert.IsType<PropertyAccess>(connectCall.Callee);
        Assert.Equal("Connect", Assert.Single(connectAccess.Names));
    }

    [Fact]
    public void Generates_InterfaceEvent_WithLuauNameAttribute_RenamesFieldAndAccessSites()
    {
        const string source = """
            interface EventObject {
                [luau_name("OnConsume")]
                event consumer(param: string);
            }

            fn on_consumer(p: string): void { }

            let eo = none as never as EventObject;
            eo.consumer += on_consumer;
            eo.consumer("abc");
            eo.consumer -= on_consumer;
            """;

        var luauTree = Utility.GetLuauAST(source, true);

        var typeAlias = Assert.IsType<TypeAlias>(luauTree.Statements[0]);
        var tableType = Assert.IsType<TableType>(typeAlias.Type);
        var eventProperty = Assert.Single(tableType.Properties);
        Assert.Equal("OnConsume", eventProperty.Name);

        var connVariable = Assert.IsType<ConstVariable>(luauTree.Statements[3]);
        var connectCall = Assert.IsType<Call>(connVariable.Initializer);
        var connectAccess = Assert.IsType<PropertyAccess>(connectCall.Callee);
        Assert.Equal("Connect", Assert.Single(connectAccess.Names));

        var eventAccess = Assert.IsType<PropertyAccess>(connectAccess.Target);
        Assert.Equal("OnConsume", Assert.Single(eventAccess.Names));
        Assert.Equal("eo", Assert.IsType<Identifier>(eventAccess.Target).Name);

        var invocationStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[4]);
        var invocationCall = Assert.IsType<Call>(invocationStatement.Expression);
        var invocationAccess = Assert.IsType<PropertyAccess>(invocationCall.Callee);
        Assert.IsType<PropertyAccess>(invocationAccess.Target);
        Assert.Equal("Fire", Assert.Single(invocationAccess.Names));

        var disconnectStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements[5]);
        var disconnectCall = Assert.IsType<Call>(disconnectStatement.Expression);
        var disconnectAccess = Assert.IsType<PropertyAccess>(disconnectCall.Callee);
        Assert.Equal("Disconnect", Assert.Single(disconnectAccess.Names));
        Assert.Equal(connVariable.Name, Assert.IsType<Identifier>(disconnectAccess.Target).Name);
    }

    [Fact]
    public void Generates_GlobalEvent_WithLuauNameAttribute_HasNoEffectOnGeneratedName()
    {
        const string source = """
            [luau_name("OnConsume")]
            event my_event(param: string);
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var variable = Assert.IsType<ConstVariable>(Assert.Single(luauTree.Statements));
        Assert.Equal("my_event", variable.Name);
    }

    /// <remarks>
    ///     Regression test: a chained access like <c>a.b.c</c> used to only rename the last step, silently
    ///     leaving an earlier step's own name unrenamed - harmless when the unrenamed step happened to match
    ///     Luau's own spelling already, but wrong (and undetected, since nothing typo'd the Luau side to
    ///     compare against) the moment a real one, like Roblox's <c>AbsolutePosition</c>, needed the rename
    ///     too. See <see cref="LuauGenerator.GenerateRenamedAccess" />.
    /// </remarks>
    [Fact]
    public void Generates_ChainedMemberAccess_RenamesEveryStepWithALuauNameAttribute()
    {
        const string source = """
            declare interface Inner {
                [luau_name("Y")]
                y: number;
            }
            declare interface Outer {
                [luau_name("InnerValue")]
                inner: Inner;
            }
            let outer = none as never as Outer;
            let value = outer.inner.y;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        var access = Assert.IsType<PropertyAccess>(variable.Initializer);
        Assert.Equal(["InnerValue", "Y"], access.Names);
    }

    [Fact]
    public void Generates_Match_EmptyArms_ReturnsNil()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 {}");
        var variable = Assert.IsType<ConstVariable>(Assert.Single(luauTree.Statements));
        Assert.IsType<NilLiteral>(variable.Initializer);
    }

    [Fact]
    public void Generates_Match_SingleWildcardArm_JustEmitsBody()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { _ -> 42 }");
        var variable = Assert.IsType<ConstVariable>(Assert.Single(luauTree.Statements));
        Assert.Equal("m", variable.Name);
        Assert.Equal(42, Assert.IsType<NumberLiteral>(variable.Initializer).Value);
    }

    [Fact]
    public void Generates_Match_LiteralArms_BuildsIfElseIfChain()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { 0 -> \"a\", 1 -> \"b\", 2 -> \"c\", _ -> \"d\" }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);

        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("==", condition.Operator);
        Assert.Equal(0, Assert.IsType<NumberLiteral>(condition.Right).Value);

        Assert.Equal(2, ifStatement.ElseIfBranches.Count);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(Assert.IsType<BinaryOperator>(ifStatement.ElseIfBranches[0].Condition).Right).Value);
        Assert.Equal(2, Assert.IsType<NumberLiteral>(Assert.IsType<BinaryOperator>(ifStatement.ElseIfBranches[1].Condition).Right).Value);
        Assert.NotNull(ifStatement.ElseBranch);
    }

    [Fact]
    public void Generates_Match_NullPattern_ComparesSubjectToNil()
    {
        var luauTree = Utility.GetLuauAST("let m = match none { none -> 1, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("==", condition.Operator);
        Assert.IsType<NilLiteral>(condition.Right);
    }

    [Fact]
    public void Generates_Match_Guard_CombinesWithPatternCondition()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { 0 when true -> \"a\", _ -> \"b\" }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var patternCondition = Assert.IsType<BinaryOperator>(condition.Left);
        Assert.Equal(0, Assert.IsType<NumberLiteral>(patternCondition.Right).Value);
        Assert.IsType<BooleanLiteral>(condition.Right);
        Assert.NotNull(ifStatement.ElseBranch);
    }

    [Fact]
    public void Generates_Match_GuardOnIdentifierPattern_SubstitutesSubjectInCondition_AndBindsInBody()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { n when n > 0 -> n, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal(">", condition.Operator);
        Assert.Equal("_subject", Assert.IsType<Identifier>(condition.Left).Name);
        Assert.Equal(0, Assert.IsType<NumberLiteral>(condition.Right).Value);

        var binding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements[0]);
        Assert.Equal("n", binding.Name);
        Assert.Equal("_subject", Assert.IsType<Identifier>(binding.Initializer).Name);
    }

    [Fact]
    public void Generates_Match_GuardOnArrayPattern_SubstitutesElementAccessesInCondition()
    {
        var luauTree = Utility.GetLuauAST("let m = match [1, 2] { [a, b] when a > b -> a, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var guardCondition = Assert.IsType<BinaryOperator>(condition.Right);
        Assert.Equal(">", guardCondition.Operator);

        var left = Assert.IsType<ElementAccess>(guardCondition.Left);
        Assert.Equal("_subject", Assert.IsType<Identifier>(left.Target).Name);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(left.Index).Value);

        var right = Assert.IsType<ElementAccess>(guardCondition.Right);
        Assert.Equal("_subject", Assert.IsType<Identifier>(right.Target).Name);
        Assert.Equal(2, Assert.IsType<NumberLiteral>(right.Index).Value);
    }

    [Fact]
    public void Generates_Match_GuardOnObjectPattern_SubstitutesPropertyAccessesInCondition()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { { x } when x > 0 -> x, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        var guardCondition = Assert.IsType<BinaryOperator>(condition.Right);

        var left = Assert.IsType<PropertyAccess>(guardCondition.Left);
        Assert.Equal("_subject", Assert.IsType<Identifier>(left.Target).Name);
        Assert.Equal(["x"], left.Names);
    }

    [Fact]
    public void Generates_Match_OrPattern_AlternativesSharingBindingName_DeclaresBinding()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { let x | let x -> x, _ -> 0 }");

        var binding = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("x", binding.Name);
        Assert.Equal("_subject", Assert.IsType<Identifier>(binding.Initializer).Name);
    }

    [Fact]
    public void Generates_Match_IdentifierPattern_BindsSubjectAndIsIrrefutable()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { n -> n }");
        Assert.DoesNotContain(luauTree.Statements, s => s is IfStatement);

        var binding = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("n", binding.Name);
        Assert.Equal("_subject", Assert.IsType<Identifier>(binding.Initializer).Name);
    }

    [Fact]
    public void Generates_Match_LetPattern_BindsSubjectAndIsIrrefutable()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { let a -> a }");
        Assert.DoesNotContain(luauTree.Statements, s => s is IfStatement);

        var binding = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("a", binding.Name);
    }

    [Fact]
    public void Generates_Match_InSiblingFunctionBodies_DoesNotSuffixAcrossUnrelatedScopes()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn fn1(): void { let m = match 1 { n when number -> n, _ -> 0 } }
            fn fn2(): void { let m = match 1 { n when number -> n, _ -> 0 } }
            """,
            true
        );

        var fn1 = Assert.IsType<Function>(luauTree.Statements[0]);
        var fn1Match = Assert.IsType<LocalVariable>(fn1.Body.Statements[1]);
        Assert.Equal("_match", fn1Match.Name);

        var fn2 = Assert.IsType<Function>(luauTree.Statements[1]);
        var fn2Match = Assert.IsType<LocalVariable>(fn2.Body.Statements[1]);
        Assert.Equal("_match", fn2Match.Name);
    }

    [Fact]
    public void Generates_Match_TwiceInSameFunctionBody_StillSuffixesCollision()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn fn1(): void {
                let a = match 1 { n when number -> n, _ -> 0 };
                let b = match 2 { n when number -> n, _ -> 0 };
            }
            """,
            true
        );

        var fn1 = Assert.IsType<Function>(luauTree.Statements[0]);
        var firstMatch = Assert.IsType<LocalVariable>(fn1.Body.Statements[1]);
        Assert.Equal("_match", firstMatch.Name);

        var secondMatch = Assert.IsType<LocalVariable>(fn1.Body.Statements[5]);
        Assert.Equal("_match_1", secondMatch.Name);
    }

    [Fact]
    public void Generates_Match_TypedPattern_ChecksTypeofAndBinds()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { n when number -> n, _ -> 0 }", true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("==", condition.Operator);
        var typeofCall = Assert.IsType<Call>(condition.Left);
        Assert.Equal("typeof", Assert.IsType<Identifier>(typeofCall.Callee).Name);
        Assert.Equal("number", Assert.IsType<StringLiteral>(condition.Right).Value);

        var binding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements[0]);
        Assert.Equal("n", binding.Name);
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnInterface_ChecksRequiredFieldsStructurally()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Foo { field: number }
            let x = 1 as never;
            let m = match x { f when Foo -> 1, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var typeofCondition = Assert.IsType<BinaryOperator>(condition.Left);
        Assert.Equal("==", typeofCondition.Operator);
        Assert.Equal("table", Assert.IsType<StringLiteral>(typeofCondition.Right).Value);

        var fieldCondition = Assert.IsType<BinaryOperator>(condition.Right);
        Assert.Equal("~=", fieldCondition.Operator);
        var fieldAccess = Assert.IsType<PropertyAccess>(fieldCondition.Left);
        Assert.Equal(["field"], fieldAccess.Names);
        Assert.IsType<NilLiteral>(fieldCondition.Right);
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnInstanceType_ChecksIsA()
    {
        var luauTree = Utility.GetLuauAST(
            """
            let x = 1 as never;
            let m = match x { inst when Model -> 1, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var typeofCondition = Assert.IsType<BinaryOperator>(condition.Left);
        Assert.Equal("==", typeofCondition.Operator);
        Assert.Equal("Instance", Assert.IsType<StringLiteral>(typeofCondition.Right).Value);

        var isACall = Assert.IsType<Call>(condition.Right);
        Assert.True(isACall.IsMethod);
        var isACallee = Assert.IsType<PropertyAccess>(isACall.Callee);
        Assert.Single(isACallee.Names);
        Assert.Equal("IsA", isACallee.Names[0]);
        Assert.Equal("Model", Assert.IsType<StringLiteral>(Assert.Single(isACall.Arguments)).Value);
    }

    [Fact]
    public void Generates_Match_TypePattern_ChecksTypeofWithoutBinding()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Foo { field: number }
            let x = 1 as never;
            let m = match x { Foo {} -> 1, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("==", condition.Operator);
        var typeofCall = Assert.IsType<Call>(condition.Left);
        Assert.Equal("typeof", Assert.IsType<Identifier>(typeofCall.Callee).Name);
        Assert.Equal("table", Assert.IsType<StringLiteral>(condition.Right).Value);

        // unlike a typed pattern, a bare type pattern captures nothing - the arm body has no binding to
        // emit, just the assignment of the arm's result
        Assert.Single(ifStatement.ThenBranch.Statements);
    }

    [Fact]
    public void Generates_Match_TypePattern_OnInterface_BindsObjectFieldsWithoutOuterBinding()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Foo { field: number }
            let x = 1 as never;
            let m = match x { Foo { field } -> field, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("==", condition.Operator);

        var binding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements[0]);
        Assert.Equal("field", binding.Name);
        var access = Assert.IsType<PropertyAccess>(binding.Initializer);
        Assert.Equal(["field"], access.Names);
    }

    [Fact]
    public void Generates_Match_TypePattern_OnInstanceType_ChecksIsA()
    {
        var luauTree = Utility.GetLuauAST(
            """
            let x = 1 as never;
            let m = match x { Model {} -> 1, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var typeofCondition = Assert.IsType<BinaryOperator>(condition.Left);
        Assert.Equal("==", typeofCondition.Operator);
        Assert.Equal("Instance", Assert.IsType<StringLiteral>(typeofCondition.Right).Value);

        var isACall = Assert.IsType<Call>(condition.Right);
        Assert.True(isACall.IsMethod);
        var isACallee = Assert.IsType<PropertyAccess>(isACall.Callee);
        Assert.Single(isACallee.Names);
        Assert.Equal("IsA", isACallee.Names[0]);
        Assert.Equal("Model", Assert.IsType<StringLiteral>(Assert.Single(isACall.Arguments)).Value);

        // unlike a typed pattern, a bare type pattern captures nothing - the arm body has no binding to
        // emit, just the assignment of the arm's result
        Assert.Single(ifStatement.ThenBranch.Statements);
    }

    [Fact]
    public void Generates_Match_RangePattern_ChecksTypeofAndBounds()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { 0..5 -> 1, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);
    }

    [Fact]
    public void Generates_Match_ArrayPattern_ChecksTypeofAndElements()
    {
        var luauTree = Utility.GetLuauAST("let m = match [1, 2] { [a, b] -> a, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        Assert.IsType<BinaryOperator>(ifStatement.Condition);

        var binding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements[0]);
        Assert.Equal("a", binding.Name);
        var access = Assert.IsType<ElementAccess>(binding.Initializer);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(access.Index).Value);
    }

    [Fact]
    public void Generates_Match_ObjectPattern_ChecksTypeofAndFields()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Box { value: number }
            let box = new Box { value: 1 };
            let m = match box { { value } -> value, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        Assert.IsType<BinaryOperator>(ifStatement.Condition);

        var binding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements[0]);
        Assert.Equal("value", binding.Name);
        var access = Assert.IsType<PropertyAccess>(binding.Initializer);
        Assert.Equal(["value"], access.Names);
    }

    [Fact]
    public void Generates_Match_NestedArrayInsideObjectInsideTypedPattern()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Foo { items: number[] }
            let foo = new Foo { items: [1, 2] };
            let m = match foo { f when Foo { items: [first, ..rest] } -> first, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var firstBinding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements.First(s => s is ConstVariable { Name: "first" }));
        var elementAccess = Assert.IsType<ElementAccess>(firstBinding.Initializer);
        var itemsAccess = Assert.IsType<PropertyAccess>(elementAccess.Target);
        Assert.Equal(["items"], itemsAccess.Names);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(elementAccess.Index).Value);

        Assert.Contains(ifStatement.ThenBranch.Statements, s => s is ConstVariable { Name: "rest" });
    }

    [Fact]
    public void Generates_Match_ObjectPattern_UsesLuauNameForRenamedField()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Box {
                [luau_name("Value")]
                value: number
            }
            let box = new Box { value: 1 };
            let m = match box { { value } -> value, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var binding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements[0]);
        Assert.Equal("value", binding.Name);
        var access = Assert.IsType<PropertyAccess>(binding.Initializer);
        Assert.Equal(["Value"], access.Names);
    }

    [Fact]
    public void Generates_Match_OrPattern_CombinesConditionsWithOr()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { 0 | 1 -> 0, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("or", condition.Operator);
    }

    [Fact]
    public void Generates_Match_AndPattern_CombinesTypeofWithGuard_SubstitutingSubject()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { n when number & n > 0 -> n, _ -> 0 }", true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var typeofCondition = Assert.IsType<BinaryOperator>(condition.Left);
        var typeofCall = Assert.IsType<Call>(typeofCondition.Left);
        Assert.Equal("typeof", Assert.IsType<Identifier>(typeofCall.Callee).Name);

        var guardCondition = Assert.IsType<BinaryOperator>(condition.Right);
        Assert.Equal(">", guardCondition.Operator);
        Assert.Equal("_subject", Assert.IsType<Identifier>(guardCondition.Left).Name);

        var binding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements[0]);
        Assert.Equal("n", binding.Name);
    }

    [Fact]
    public void Generates_Match_MalformedPattern_RecoversAsNullPatternComparison()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { ) -> 1, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("==", condition.Operator);
        Assert.IsType<NilLiteral>(condition.Right);
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnUnionType_CombinesTypeofChecksWithOr()
    {
        var luauTree = Utility.GetLuauAST("let x = 1 as never; let m = match x { n when number | string -> 1, _ -> 0 }", true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("or", condition.Operator);

        var left = Assert.IsType<BinaryOperator>(condition.Left);
        Assert.Equal("number", Assert.IsType<StringLiteral>(left.Right).Value);

        var right = Assert.IsType<BinaryOperator>(condition.Right);
        Assert.Equal("string", Assert.IsType<StringLiteral>(right.Right).Value);
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnLiteralTypeUnion_MapsEachMemberToTypeofString()
    {
        var luauTree = Utility.GetLuauAST("let x = 1 as never; let m = match x { n when 1 | \"a\" | true -> 1, _ -> 0 }", true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);

        var typeofStrings = new List<string>();

        collectTypeofStrings(ifStatement.Condition);
        Assert.Equal(["number", "string", "boolean"], typeofStrings);
        return;

        void collectTypeofStrings(LuauExpression expr)
        {
            while (true)
            {
                switch (expr)
                {
                    case BinaryOperator { Operator: "or" } or:
                        collectTypeofStrings(or.Left);
                        expr = or.Right;
                        continue;
                    case BinaryOperator { Operator: "==", Right: StringLiteral literal }:
                        typeofStrings.Add(literal.Value);
                        break;
                }

                break;
            }
        }
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnIntersectionType_CombinesFieldChecksWithAnd()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Named { name: string }
            interface Aged { age: number }
            let x = 1 as never;
            let m = match x { n when Named & Aged -> 1, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[4]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var fieldChecks = new List<string>();

        collectFieldAccesses(condition);
        Assert.Equal(["name", "age"], fieldChecks);
        return;

        void collectFieldAccesses(LuauExpression expr)
        {
            while (true)
            {
                switch (expr)
                {
                    case BinaryOperator { Operator: "and" } and:
                        collectFieldAccesses(and.Left);
                        expr = and.Right;
                        continue;
                    case BinaryOperator { Operator: "~=", Left: PropertyAccess property }:
                        fieldChecks.Add(property.Names.Single());
                        break;
                }

                break;
            }
        }
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnInstantiatedInterface_ChecksRequiredFieldsStructurally()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Box<T> { value: T }
            let x = 1 as never;
            let m = match x { n when Box<number> -> 1, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var fieldCondition = Assert.IsType<BinaryOperator>(condition.Right);
        Assert.Equal("~=", fieldCondition.Operator);
        var fieldAccess = Assert.IsType<PropertyAccess>(fieldCondition.Left);
        Assert.Equal(["value"], fieldAccess.Names);
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnBoolType_ChecksTypeofBoolean()
    {
        var luauTree = Utility.GetLuauAST("let x = 1 as never; let m = match x { n when bool -> 1, _ -> 0 }", true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("boolean", Assert.IsType<StringLiteral>(condition.Right).Value);
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnFunctionType_ChecksTypeofFunction()
    {
        var luauTree = Utility.GetLuauAST("let x = 1 as never; let m = match x { n when fn(): void -> 1, _ -> 0 }", true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("function", Assert.IsType<StringLiteral>(condition.Right).Value);
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnArrayType_ChecksTypeofTable()
    {
        var luauTree = Utility.GetLuauAST("let x = 1 as never; let m = match x { n when number[] -> 1, _ -> 0 }", true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("table", Assert.IsType<StringLiteral>(condition.Right).Value);
    }

    [Fact]
    public void Generates_Match_TypedPattern_OnUnknownType_AlwaysMatches()
    {
        var luauTree = Utility.GetLuauAST("let x = 1 as never; let m = match x { n when unknown -> 1, _ -> 0 }", true);
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        Assert.True(Assert.IsType<BooleanLiteral>(ifStatement.Condition).Value);
    }

    [Fact]
    public void Generates_Match_LiteralPattern_WithDecimalValue()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1.5 { 1.5 -> 1, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal(1.5, Assert.IsType<NumberLiteral>(condition.Right).Value);
    }

    [Fact]
    public void Generates_Match_GuardOnLetPattern_SubstitutesSubjectInCondition()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { let a when a > 0 -> a, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal(">", condition.Operator);
        Assert.Equal("_subject", Assert.IsType<Identifier>(condition.Left).Name);
    }

    [Fact]
    public void Generates_Match_GuardOnTypedObjectPattern_SubstitutesFieldBindingInCondition()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Foo { field: number }
            let x = 1 as never;
            let m = match x { f when Foo { field } when field > 0 -> 1, _ -> 0 }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[3]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var guardCondition = Assert.IsType<BinaryOperator>(condition.Right);
        Assert.Equal(">", guardCondition.Operator);
        var fieldAccess = Assert.IsType<PropertyAccess>(guardCondition.Left);
        Assert.Equal(["field"], fieldAccess.Names);
    }

    [Fact]
    public void Generates_Match_GuardOnArrayRestPattern_SubstitutesRestSliceInCondition()
    {
        var luauTree = Utility.GetLuauAST("let m = match [1, 2, 3] { [a, ..rest] when rest[1] > 0 -> a, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var guardCondition = Assert.IsType<BinaryOperator>(condition.Right);
        var elementAccess = Assert.IsType<ElementAccess>(guardCondition.Left);
        Assert.IsType<Call>(elementAccess.Target);
    }

    [Fact]
    public void Generates_Match_GuardOnTuplePattern_SubstitutesElementAccessesInCondition()
    {
        var luauTree = Utility.GetLuauAST("let m = match (1, 2) { (a, b) when a > b -> a, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var guardCondition = Assert.IsType<BinaryOperator>(condition.Right);
        var left = Assert.IsType<ElementAccess>(guardCondition.Left);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(left.Index).Value);
        var right = Assert.IsType<ElementAccess>(guardCondition.Right);
        Assert.Equal(2, Assert.IsType<NumberLiteral>(right.Index).Value);
    }

    [Fact]
    public void Generates_Match_ArmGuardOnArrayContainingAndPattern_StillAppliesElementGuard()
    {
        var luauTree = Utility.GetLuauAST("let m = match [1] { [n & n > 0] when true -> n, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("and", condition.Operator);

        var withoutArmGuard = Assert.IsType<BinaryOperator>(condition.Left);
        var elementGuard = Assert.IsType<BinaryOperator>(withoutArmGuard.Right);
        Assert.Equal(">", elementGuard.Operator);
        Assert.IsType<ElementAccess>(elementGuard.Left);
    }

    [Fact]
    public void Generates_Match_GuardOnOrPattern_SubstitutesSharedBindingInCondition()
    {
        var luauTree = Utility.GetLuauAST("let m = match 1 { let a | let a when a > 0 -> a, _ -> 0 }");
        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal(">", condition.Operator);
        Assert.Equal("_subject", Assert.IsType<Identifier>(condition.Left).Name);
    }

    [Fact]
    public void Generates_FunctionParameter_WithDefaultValue_WrapsTypeAsOptional()
    {
        var luauTree = Utility.GetLuauAST("fn greet(name: string = \"world\") { return name }");
        var fn = Assert.IsType<Function>(luauTree.Statements.Single());
        var parameter = fn.Parameters.Single();

        var optional = Assert.IsType<OptionalType>(parameter.DeclaredType);
        var inner = Assert.IsType<PrimitiveType>(optional.Inner);
        Assert.Equal(PrimitiveTypeKind.String, inner.Kind);
    }

    [Fact]
    public void Generates_FunctionParameter_AlreadyOptionalWithDefaultValue_DoesNotDoubleWrap()
    {
        var luauTree = Utility.GetLuauAST("fn f(x: number? = none) { return x }");
        var fn = Assert.IsType<Function>(luauTree.Statements.Single());
        var parameter = fn.Parameters.Single();

        var optional = Assert.IsType<OptionalType>(parameter.DeclaredType);
        var inner = Assert.IsType<PrimitiveType>(optional.Inner);
        Assert.Equal(PrimitiveTypeKind.Number, inner.Kind);
    }

    [Fact]
    public void Generates_FunctionParameter_WithDefaultValue_EmitsNilGuard()
    {
        var luauTree = Utility.GetLuauAST("fn abc(param = 69) -> print(param);");
        var fn = Assert.IsType<Function>(luauTree.Statements.Single());

        var guard = Assert.IsType<IfStatement>(fn.Body.Statements[0]);
        var condition = Assert.IsType<BinaryOperator>(guard.Condition);
        Assert.Equal("==", condition.Operator);
        Assert.Equal("param", Assert.IsType<Identifier>(condition.Left).Name);
        Assert.IsType<NilLiteral>(condition.Right);

        var assignment = Assert.IsType<ExpressionStatement>(Assert.Single(guard.ThenBranch.Statements));
        var binaryOperator = Assert.IsType<BinaryOperator>(assignment.Expression);
        Assert.Equal("=", binaryOperator.Operator);
        Assert.Equal("param", Assert.IsType<Identifier>(binaryOperator.Left).Name);
        Assert.Equal(69, Assert.IsType<NumberLiteral>(binaryOperator.Right).Value);

        var @return = Assert.IsType<Return>(fn.Body.Statements[1]);
        var printCall = Assert.IsType<Call>(@return.Expression);
        Assert.Equal("param", Assert.IsType<Identifier>(Assert.Single(printCall.Arguments)).Name);
    }

    [Fact]
    public void Generates_FunctionParameter_WithoutDefaultValue_EmitsNoGuard()
    {
        var luauTree = Utility.GetLuauAST("fn abc(param: number) -> print(param);");
        var fn = Assert.IsType<Function>(luauTree.Statements.Single());
        Assert.DoesNotContain(fn.Body.Statements, s => s is IfStatement);
    }

    [Fact]
    public void Generates_ImplementMethodParameter_WithDefaultValue_EmitsNilGuard()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Container { value: number }
            trait Display { fn display(depth: number): void }
            implement Display for Container {
                fn display(depth = 1) -> print(depth * value);
            }
            """,
            true
        );

        var displayFunction = Assert.IsType<Function>(luauTree.Statements[^1]);
        var guard = Assert.IsType<IfStatement>(displayFunction.Body.Statements[0]);
        var condition = Assert.IsType<BinaryOperator>(guard.Condition);
        Assert.Equal("depth", Assert.IsType<Identifier>(condition.Left).Name);
        Assert.IsType<NilLiteral>(condition.Right);
    }

    [Fact]
    public void Generates_EnumDeclaration_WithoutTypeCheck_EmitsEmptyPlaceholder()
    {
        var luauTree = Utility.GetLuauAST("enum Abc { A, B }");
        var variable = Assert.IsType<ConstVariable>(Assert.Single(luauTree.Statements));
        Assert.Equal("_", variable.Name);
        Assert.IsType<NilLiteral>(variable.Initializer);
    }

    [Fact]
    public void Generates_StandaloneBlock_WrapsInDoStatement()
    {
        var luauTree = Utility.GetLuauAST("{ let x = 1 }");
        var doStatement = Assert.IsType<Do>(Assert.Single(luauTree.Statements));

        var variable = Assert.IsType<ConstVariable>(Assert.Single(doStatement.Body.Statements));
        Assert.Equal("x", variable.Name);
    }

    [Fact]
    public void Generates_ForLoop_OverArray_WithTwoNames_ReversesToIndexValueOrder()
    {
        var luauTree = Utility.GetLuauAST("for i, x : [1, 2, 3] { }", true);
        var forStmt = Assert.IsType<ForStatement>(luauTree.Statements.First());
        Assert.Equal(["x", "i"], forStmt.Names);
    }

    [Fact]
    public void Generates_ForLoop_OverObjectValue_SingleName_PrependsKeyPlaceholder()
    {
        var luauTree = Utility.GetLuauAST(
            "interface Data { a: number, b: string } for v : new Data { a: 1, b: \"hi\" } { }",
            true
        );

        var forStmt = Assert.IsType<ForStatement>(luauTree.Statements.Last());
        Assert.Equal(["_", "v"], forStmt.Names);
    }

    [Fact]
    public void Generates_VariableDeclaration_UnwrapsParenthesizedInitializer()
    {
        var luauTree = Utility.GetLuauAST("let x = (1)");
        var variable = Assert.IsType<ConstVariable>(Assert.Single(luauTree.Statements));
        Assert.IsType<NumberLiteral>(variable.Initializer);
    }

    [Fact]
    public void Generates_UnaryOperator_Minus_PassesThroughUnchanged()
    {
        var luauTree = Utility.GetLuauAST("let x = 1; -x");
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var unary = Assert.IsType<UnaryOperator>(variable.Initializer);
        Assert.Equal("-", unary.Operator);
        Assert.Equal("x", Assert.IsType<Identifier>(unary.Operand).Name);
    }

    [Fact]
    public void Generates_ArrayDestructuring_AsIndexedConsts()
    {
        var luauTree = Utility.GetLuauAST("let array = [1, 2]; let [first, second] = array;", true);
        Assert.Equal(3, luauTree.Statements.Count);

        var first = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        Assert.Equal("first", first.Name);
        var firstAccess = Assert.IsType<ElementAccess>(first.Initializer);
        Assert.Equal("array", Assert.IsType<Identifier>(firstAccess.Target).Name);
        Assert.Equal(1d, Assert.IsType<NumberLiteral>(firstAccess.Index).Value);

        var second = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("second", second.Name);
        var secondAccess = Assert.IsType<ElementAccess>(second.Initializer);
        Assert.Equal(2d, Assert.IsType<NumberLiteral>(secondAccess.Index).Value);
    }

    [Fact]
    public void Generates_ArrayDestructuring_FromNonTrivialInitializer_SpillsToTemp()
    {
        var luauTree = Utility.GetLuauAST("let [first, second] = [1, 2, 3];", true);
        Assert.Equal(3, luauTree.Statements.Count);

        var temp = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.Equal("_destructure", temp.Name);
        Assert.IsType<Table>(temp.Initializer);

        var first = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var firstAccess = Assert.IsType<ElementAccess>(first.Initializer);
        Assert.Equal("_destructure", Assert.IsType<Identifier>(firstAccess.Target).Name);
    }

    [Fact]
    public void Generates_ObjectDestructuring_AsPropertyConsts()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface User { name: string, age: number }
            let user = new User { name: "Ada", age: 30 };
            let { name, age } = user;
            """,
            true
        );

        var name = Assert.IsType<ConstVariable>(luauTree.Statements[^2]);
        Assert.Equal("name", name.Name);
        var nameAccess = Assert.IsType<PropertyAccess>(name.Initializer);
        Assert.Equal("user", Assert.IsType<Identifier>(nameAccess.Target).Name);
        Assert.Equal(["name"], nameAccess.Names);

        var age = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        Assert.Equal("age", age.Name);
        Assert.Equal(["age"], Assert.IsType<PropertyAccess>(age.Initializer).Names);
    }

    [Fact]
    public void Generates_ObjectDestructuring_WithAlias_BindsUnderAliasName_ReadsOriginalProperty()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface User { age: number }
            let user = new User { age: 30 };
            let { age: userAge } = user;
            """,
            true
        );

        var userAge = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        Assert.Equal("userAge", userAge.Name);
        var access = Assert.IsType<PropertyAccess>(userAge.Initializer);
        Assert.Equal(["age"], access.Names);
    }

    [Fact]
    public void Generates_NestedObjectDestructuring_AsOneChainedPropertyAccess()
    {
        const string source = """
            interface Address { city: string }
            interface User { name: string, address: Address }
            let user = new User { name: "a", address: new Address { city: "b" } };
            let { address: { city } } = user;
            """;

        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("const city = user.address.city", rendered);
    }

    [Fact]
    public void Generates_ArrayNestedInsideObjectDestructuring_AsPropertyThenElementAccess()
    {
        const string source = """
            interface Summary { scores: number[] }
            let summary = new Summary { scores: [10, 20] };
            let { scores: [first, second] } = summary;
            """;

        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("const first = summary.scores[1]", rendered);
        Assert.Contains("const second = summary.scores[2]", rendered);
    }

    [Fact]
    public void Generates_ObjectNestedInsideArrayDestructuring_AsElementThenPropertyAccess()
    {
        const string source = """
            interface Point { x: number, y: number }
            let points = [new Point { x: 1, y: 2 }];
            let [{ x: firstX }] = points;
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var firstX = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        Assert.Equal("firstX", firstX.Name);

        var access = Assert.IsType<PropertyAccess>(firstX.Initializer);
        Assert.Equal(["x"], access.Names);
        var elementAccess = Assert.IsType<ElementAccess>(access.Target);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(elementAccess.Index).Value);
    }

    [Fact]
    public void Generates_Destructuring_NoRefutabilityGuards()
    {
        var rendered = Utility.GetLuauAST("let array = [1, 2]; let [first, second] = array;", true).Render();
        Assert.DoesNotContain("typeof", rendered);
        Assert.DoesNotContain("if ", rendered);
    }

    [Fact]
    public void Generates_ArrayDestructuringElement_WithDefault_AsNilGuard()
    {
        const string source = "let maybe_pair = [1]; let [first, second = 0] = maybe_pair;";
        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("local second = maybe_pair[2]", rendered);
        Assert.Contains("if second == nil then", rendered);
        Assert.Contains("second = 0", rendered);
    }

    [Fact]
    public void Generates_ObjectDestructuringField_WithDefault_AsNilGuard()
    {
        const string source = "interface Config { retries: number? } let config = new Config { retries: none }; let { retries = 3 } = config;";
        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("local retries = config.retries", rendered);
        Assert.Contains("if retries == nil then", rendered);
        Assert.Contains("retries = 3", rendered);
    }

    [Fact]
    public void Generates_NonDefaultedDestructuringElement_StaysConst()
    {
        const string source = "let maybe_pair = [1]; let [first, second = 0] = maybe_pair;";
        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("const first = maybe_pair[1]", rendered);
    }

    [Fact]
    public void Generates_NestedDestructuringTarget_WithDefault_GuardsSubjectBeforeDestructuring()
    {
        const string source = "let pairs = [[1, 2]]; let [[a, b] = [1, 2]] = pairs;";
        var rendered = Utility.GetLuauAST(source, true).Render();
        Assert.Contains("local _destructure = pairs[1]", rendered);
        Assert.Contains("if _destructure == nil then", rendered);
        Assert.Contains("_destructure = {1, 2}", rendered);
        Assert.Contains("const a = _destructure[1]", rendered);
        Assert.Contains("const b = _destructure[2]", rendered);
    }

    [Fact]
    public void Generates_TupleLiteral_AsTable()
    {
        var luauTree = Utility.GetLuauAST("let t = (\"abc\", 420);", true);
        var variable = Assert.IsType<ConstVariable>(Assert.Single(luauTree.Statements));
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Equal(2, table.Initializers.Count);
    }

    [Fact]
    public void Generates_TupleReturnType_RendersLuauMultiReturnSyntax()
    {
        var rendered = Utility.GetLuauAST(
            """
            fn returns_tuple: (string, number) {
                return ("abc", 420);
            }
            """,
            true
        ).Render();

        Assert.Contains("(): (string, number)", rendered);
    }

    [Fact]
    public void Generates_TupleVariableAnnotation_RendersTableUnionType()
    {
        var rendered = Utility.GetLuauAST("let t: (string, number) = (\"abc\", 420);", true).Render();
        Assert.Contains("{ string | number }", rendered);
    }

    [Fact]
    public void Generates_TupleReturn_OfLiteral_EmitsNoTableOrUnpack()
    {
        var rendered = Utility.GetLuauAST(
            """
            fn returns_tuple: (string, number) {
                return ("abc", 420);
            }
            """,
            true
        ).Render();

        Assert.Contains("return \"abc\", 420", rendered);
        Assert.DoesNotContain("table.unpack", rendered);
    }

    [Fact]
    public void Generates_TupleReturn_OfVariable_WrapsTableUnpack()
    {
        var rendered = Utility.GetLuauAST(
            """
            fn returns_tuple: (string, number) {
                let t = ("abc", 420);
                return t;
            }
            """,
            true
        ).Render();

        Assert.Contains("return table.unpack(t)", rendered);
    }

    [Fact]
    public void Generates_TupleDestructure_OfLiteral_EmitsNoTableOrMultiConst()
    {
        var rendered = Utility.GetLuauAST("let (one, two) = (\"abc\", 420);", true).Render();
        Assert.Contains("const one = \"abc\"", rendered);
        Assert.Contains("const two = 420", rendered);
        Assert.DoesNotContain("table.unpack", rendered);
    }

    [Fact]
    public void Generates_TupleDestructure_OfCall_EmitsMultiConstNoUnpack()
    {
        var rendered = Utility.GetLuauAST(
            """
            fn returns_tuple: (string, number) {
                return ("abc", 420);
            }
            let (one, two) = returns_tuple();
            """,
            true
        ).Render();

        Assert.Contains("const one, two = returns_tuple()", rendered);
        Assert.DoesNotContain("table.unpack", rendered);
    }

    [Fact]
    public void Generates_TupleDestructure_OfValue_WrapsTableUnpack()
    {
        var rendered = Utility.GetLuauAST("let t: (string, number) = (\"abc\", 420); let (one, two) = t;", true).Render();
        Assert.Contains("const one, two = table.unpack(t)", rendered);
    }

    [Fact]
    public void Generates_MatchTuplePattern_AsIndexedAccess_NoTableMoveOrSlice()
    {
        var rendered = Utility.GetLuauAST(
            """
            let t: (string, number) = ("abc", 420);
            match t {
                (a, b) -> a,
                _ -> "none",
            };
            """,
            true
        ).Render();

        Assert.Contains("typeof(t) == \"table\"", rendered);
        Assert.Contains("t[1]", rendered);
        Assert.Contains("t[2]", rendered);
        Assert.DoesNotContain("table.move", rendered);
    }

    [Fact]
    public void Generates_IsExpression_ChecksTypeofAndCastsScrutineeInThenBranch()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface SomeType {}
            let value = none as never as SomeType;
            if value is SomeType {
                print(value)
            }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var condition = Assert.IsType<BinaryOperator>(ifStatement.Condition);
        Assert.Equal("==", condition.Operator);
        var typeofCall = Assert.IsType<Call>(condition.Left);
        Assert.Equal("typeof", Assert.IsType<Identifier>(typeofCall.Callee).Name);
        Assert.Equal("table", Assert.IsType<StringLiteral>(condition.Right).Value);

        var castStatement = Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]);
        var cast = Assert.IsType<BinaryOperator>(castStatement.Expression);
        Assert.Equal("=", cast.Operator);
        Assert.Equal("value", Assert.IsType<Identifier>(cast.Left).Name);
        var typeCast = Assert.IsType<TypeCast>(cast.Right);
        Assert.Equal("value", Assert.IsType<Identifier>(typeCast.Expression).Name);
    }

    [Fact]
    public void Generates_IsExpression_WithObjectPattern_ChecksFieldsAndBindsAfterCast()
    {
        var rendered = Utility.GetLuauAST(
            """
            interface SomeType { some_field: number }
            let value = none as never as SomeType;
            if value is SomeType { some_field: 0..1 } {
                print(value.some_field)
            }
            """,
            true
        ).Render();

        Assert.Contains("typeof(value) == \"table\" and typeof(value.some_field) == \"number\" and value.some_field >= 0 and value.some_field <= 1", rendered);
        Assert.Contains("value = value :: SomeType", rendered);
    }

    [Fact]
    public void Generates_IsExpression_ObjectPatternField_BindsAfterCast()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface SomeType { some_field: number }
            let value = none as never as SomeType;
            if value is SomeType { some_field: x } {
                print(x)
            }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var castStatement = Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]);
        Assert.IsType<BinaryOperator>(castStatement.Expression);

        var binding = Assert.IsType<ConstVariable>(ifStatement.ThenBranch.Statements[1]);
        Assert.Equal("x", binding.Name);
        var access = Assert.IsType<PropertyAccess>(binding.Initializer);
        Assert.Equal(["some_field"], access.Names);
    }

    [Fact]
    public void Generates_IsExpression_AndChainedCondition_SubstitutesBindingBeforeItsDeclared()
    {
        var rendered = Utility.GetLuauAST(
            """
            interface SomeType { some_field: number }
            let value = none as never as SomeType;
            if value is SomeType { some_field: n } && n > 0 {
                print(n)
            }
            """,
            true
        ).Render();

        Assert.Contains("value.some_field > 0", rendered);
    }

    [Fact]
    public void Generates_IsExpression_NoCast_WhenScrutineeIsNotAnAssignmentTarget()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface SomeType {}
            fn get_value(): SomeType | number {
                return none as never as SomeType;
            }
            if get_value() is SomeType {
                print("matched")
            }
            """,
            true
        );

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements.OfType<IfStatement>().Single());
        Assert.Single(ifStatement.ThenBranch.Statements);
    }

    [Fact]
    public void Generates_FunctionExpression_AsAnonymousFunction()
    {
        var luauTree = Utility.GetLuauAST("let f = fn(a: number, b: number): number { return a + b; };", true);
        var declaration = Assert.IsType<ConstVariable>(luauTree.Statements.Single());
        var anonFn = Assert.IsType<AnonymousFunction>(declaration.Initializer);

        Assert.Equal(2, anonFn.Parameters.Count);
        Assert.NotNull(anonFn.ReturnType);
        Assert.Single(anonFn.Body.Statements);
    }

    [Fact]
    public void Generates_FunctionExpression_CapturesOuterVariable()
    {
        var luauTree = Utility.GetLuauAST("let x = 42; let f = fn(): number { return x + 1; };", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var declaration = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var anonFn = Assert.IsType<AnonymousFunction>(declaration.Initializer);
        var returnStatement = Assert.IsType<Return>(Assert.Single(anonFn.Body.Statements));
        var binary = Assert.IsType<BinaryOperator>(returnStatement.Expression);
        Assert.Equal("x", Assert.IsType<Identifier>(binary.Left).Name);
    }

    [Fact]
    public void Generates_FunctionExpression_ArrowBody_AsSingleReturnStatement()
    {
        var luauTree = Utility.GetLuauAST("let f = fn(x: number) -> x + 1;", true);
        var declaration = Assert.IsType<ConstVariable>(luauTree.Statements.Single());
        var anonFn = Assert.IsType<AnonymousFunction>(declaration.Initializer);

        var returnStatement = Assert.IsType<Return>(Assert.Single(anonFn.Body.Statements));
        Assert.IsType<BinaryOperator>(returnStatement.Expression);
    }

    [Fact]
    public void Generates_FunctionExpression_NestedClosure_ReturnsAnonymousFunction()
    {
        var luauTree = Utility.GetLuauAST("let make_adder = fn(x: number) -> fn(y: number): number { return x + y; };", true);
        var declaration = Assert.IsType<ConstVariable>(luauTree.Statements.Single());
        var outerFn = Assert.IsType<AnonymousFunction>(declaration.Initializer);

        var returnStatement = Assert.IsType<Return>(Assert.Single(outerFn.Body.Statements));
        Assert.IsType<AnonymousFunction>(returnStatement.Expression);
    }

    [Fact]
    public void Generates_Decorator_InlinesBodyIntoThunk_NoSeparateImplFunction()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn log(f: fn(): void, name: string): void { f(); }
            [log]
            fn do_something() { print("hi"); }
            """,
            true
        );

        Assert.DoesNotContain(luauTree.Statements, s => s is Function { Name: "_do_something_impl" });

        var wrapper = luauTree.Statements.OfType<Function>().Single(f => f.Name == "do_something");
        var returnStatement = Assert.IsType<Return>(Assert.Single(wrapper.Body.Statements));
        var call = Assert.IsType<Call>(returnStatement.Expression);
        Assert.Equal("log", Assert.IsType<Identifier>(call.Callee).Name);

        var thunk = Assert.IsType<AnonymousFunction>(call.Arguments[0]);
        var thunkStatement = Assert.IsType<ExpressionStatement>(Assert.Single(thunk.Body.Statements));
        var printCall = Assert.IsType<Call>(thunkStatement.Expression);
        Assert.Equal("print", Assert.IsType<Identifier>(printCall.Callee).Name);

        Assert.Equal("do_something", Assert.IsType<StringLiteral>(call.Arguments[1]).Value);
    }

    [Fact]
    public void Generates_Decorator_CapturesOriginalParametersAsUpvalues()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn log(f: fn(): number, name: string): number { return f(); }
            [log]
            fn add(a: number, b: number): number { return a + b; }
            """,
            true
        );

        var wrapper = luauTree.Statements.OfType<Function>().Single(f => f.Name == "add");
        Assert.Equal(["a", "b"], wrapper.Parameters.ConvertAll(p => p.Name));

        var returnStatement = Assert.IsType<Return>(Assert.Single(wrapper.Body.Statements));
        var call = Assert.IsType<Call>(returnStatement.Expression);
        var thunk = Assert.IsType<AnonymousFunction>(call.Arguments[0]);
        var thunkReturn = Assert.IsType<Return>(Assert.Single(thunk.Body.Statements));
        var sum = Assert.IsType<BinaryOperator>(thunkReturn.Expression);

        Assert.Equal("a", Assert.IsType<Identifier>(sum.Left).Name);
        Assert.Equal("b", Assert.IsType<Identifier>(sum.Right).Name);
    }

    [Fact]
    public void Generates_ChainedDecorators_LaterAttributeWrapsEarlierOne()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn a(f: fn(): void, name: string): void { f(); }
            fn b(f: fn(): void, name: string): void { f(); }
            [a, b]
            fn do_something() { }
            """,
            true
        );

        var wrapper = luauTree.Statements.OfType<Function>().Single(f => f.Name == "do_something");
        var returnStatement = Assert.IsType<Return>(Assert.Single(wrapper.Body.Statements));
        var outerCall = Assert.IsType<Call>(returnStatement.Expression);
        Assert.Equal("b", Assert.IsType<Identifier>(outerCall.Callee).Name);

        var innerThunk = Assert.IsType<AnonymousFunction>(outerCall.Arguments[0]);
        var innerReturn = Assert.IsType<Return>(Assert.Single(innerThunk.Body.Statements));
        var innerCall = Assert.IsType<Call>(innerReturn.Expression);
        Assert.Equal("a", Assert.IsType<Identifier>(innerCall.Callee).Name);
    }

    [Fact]
    public void Generates_ChainedDecoratorFactories_EachHoistsToItsOwnDeclarationTimeLocal()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn a(ctx: string) -> fn(f: fn(): void, name: string): void {
                f();
            };
            fn b(ctx: string) -> fn(f: fn(): void, name: string): void {
                f();
            };
            [a("x"), b("y")]
            fn do_something() { }
            """,
            true
        );

        var decoratorA = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        Assert.Equal("_do_something_decorator", decoratorA.Name);
        var aCall = Assert.IsType<Call>(decoratorA.Initializer);
        Assert.Equal("a", Assert.IsType<Identifier>(aCall.Callee).Name);

        var decoratorB = Assert.IsType<ConstVariable>(luauTree.Statements[3]);
        Assert.Equal("_do_something_decorator_1", decoratorB.Name);
        var bCall = Assert.IsType<Call>(decoratorB.Initializer);
        Assert.Equal("b", Assert.IsType<Identifier>(bCall.Callee).Name);

        var wrapper = luauTree.Statements.OfType<Function>().Single(f => f.Name == "do_something");
        var returnStatement = Assert.IsType<Return>(Assert.Single(wrapper.Body.Statements));
        var outerCall = Assert.IsType<Call>(returnStatement.Expression);
        Assert.Equal("_do_something_decorator_1", Assert.IsType<Identifier>(outerCall.Callee).Name);

        var innerThunk = Assert.IsType<AnonymousFunction>(outerCall.Arguments[0]);
        var innerReturn = Assert.IsType<Return>(Assert.Single(innerThunk.Body.Statements));
        var innerCall = Assert.IsType<Call>(innerReturn.Expression);
        Assert.Equal("_do_something_decorator", Assert.IsType<Identifier>(innerCall.Callee).Name);
    }

    [Fact]
    public void Generates_DecoratorFactory_MatchesIssueExample()
    {
        var rendered = Utility.GetLuauAST(
            """
            fn log(ctx: string) -> fn<T>(f: fn(): T, name: string): T {
                let result = f();
                return result;
            };

            [log("info")]
            fn do_something -> print("did something!");
            """,
            true
        ).Render();

        // The factory invocation must run exactly once, at declaration time - not on every call to
        // do_something() - so it's hoisted into its own local ahead of the wrapper function (#156).
        Assert.Contains("const _do_something_decorator = log(\"info\")", rendered);
        Assert.Contains("const function do_something()", rendered);
        Assert.Contains("return _do_something_decorator(function()", rendered);
        Assert.Contains("return print(\"did something!\")", rendered);
        Assert.DoesNotContain("_do_something_impl", rendered);
        Assert.DoesNotContain("log(\"info\")(function()", rendered);
    }

    [Fact]
    public void Generates_DecoratorFactory_EvaluatesOnceAcrossMultipleCalls()
    {
        var rendered = Utility.GetLuauAST(
            """
            fn log(ctx: string) -> fn<T>(f: fn(): T, name: string): T {
                let result = f();
                return result;
            };

            [log("info")]
            fn do_something -> print("did something!");

            do_something();
            do_something();
            """,
            true
        ).Render();

        // 'log("info")' produces the decorator; that construction is call-independent, so it must appear
        // exactly once in the output regardless of how many times do_something() is actually called.
        Assert.Equal(1, rendered.Split("log(\"info\")").Length - 1);
    }

    [Fact]
    public void Generates_InterfaceDecorator_ConstructionIsUnwrapped()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn validate(): void { }
            [validate]
            interface Foo { x: number }
            let foo = new Foo { x: 1 };
            """,
            true
        );

        var declaration = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        Assert.IsType<Table>(declaration.Initializer);
    }

    [Fact]
    public void Generates_InterfaceDecorator_EveryConstructionSiteIsUnwrapped()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn validate(): void { }
            [validate]
            interface Foo { x: number }
            let a = new Foo { x: 1 };
            let b = new Foo { x: 2 };
            """,
            true
        );

        var declarations = luauTree.Statements.OfType<ConstVariable>().Where(d => d.Name is "a" or "b").ToList();
        Assert.Equal(2, declarations.Count);
        Assert.All(declarations, d => Assert.IsType<Table>(d.Initializer));
    }

    [Fact]
    public void Generates_TopLevelEventDecorator_ConstructionIsUnwrapped()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn log_event(): void { }
            [log_event]
            event scored(points: number);
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "scored");
        var call = Assert.IsType<Call>(declaration.Initializer);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(["Event", "new"], callee.Names);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void Generates_PropertyDecorator_FieldValueIsUnwrapped()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn clamp(): void { }
            interface Account {
                [clamp]
                balance: number
            }
            let a = new Account { balance: 10 };
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "a");
        var table = Assert.IsType<Table>(declaration.Initializer);
        var propertyInitializer = Assert.IsType<PropertyTableInitializer>(Assert.Single(table.Initializers));
        Assert.Equal("balance", propertyInitializer.PropertyName);
        Assert.IsType<NumberLiteral>(propertyInitializer.Value);
    }

    [Fact]
    public void Generates_PropertyDecorator_ShorthandInitializer_IsUnwrapped()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn clamp(): void { }
            interface Account {
                [clamp]
                balance: number
            }
            let balance = 10;
            let a = new Account { balance };
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "a");
        var table = Assert.IsType<Table>(declaration.Initializer);
        var propertyInitializer = Assert.IsType<PropertyTableInitializer>(Assert.Single(table.Initializers));
        Assert.IsType<Identifier>(propertyInitializer.Value);
    }

    [Fact]
    public void Generates_IntrinsicAttribute_OnEvent_IsNotWrappedAsDecorator()
    {
        var luauTree = Utility.GetLuauAST(
            """
            [luau_name("NotUsed")]
            event my_event(param: string);
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "my_event");
        var call = Assert.IsType<Call>(declaration.Initializer);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(["Event", "new"], callee.Names);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void Generates_MetadataOnlyDecorator_OnFunction_IsNotWrapped()
    {
        var luauTree = Utility.GetLuauAST(
            """
            [metadata_only]
            fn replicated(): void {}

            [replicated]
            fn greet(name: string) {
                print(name);
            }
            """,
            true
        );

        var wrapper = luauTree.Statements.OfType<Function>().Single(f => f.Name == "greet");
        Assert.DoesNotContain(wrapper.Body.Statements, s => s is Return { Expression: Call });
        var printCall = Assert.IsType<Call>(Assert.IsType<ExpressionStatement>(Assert.Single(wrapper.Body.Statements)).Expression);
        Assert.Equal("print", Assert.IsType<Identifier>(printCall.Callee).Name);
    }

    [Fact]
    public void Generates_MixedDecorators_OnlyWrapsWithNonMetadataOnlyOne()
    {
        var rendered = Utility.GetLuauAST(
            """
            [metadata_only]
            fn replicated(): void {}

            fn log(f: fn(): void, name: string): void { f(); }

            [replicated, log]
            fn mixed() { print("mixed"); }
            """,
            true
        ).Render();

        Assert.Contains("const function mixed()", rendered);
        Assert.Contains("return log(function()", rendered);
        Assert.DoesNotContain("replicated(function()", rendered);
    }

    [Fact]
    public void Generates_GetMetadata_FoldsToConstantArgsArray()
    {
        var luauTree = Utility.GetLuauAST(
            """
            enum Level { Low = 1, High = 2 }
            fn tag(level: Level): void { }
            interface Account {
                [tag(Level::High)]
                balance: number
            }
            let meta = get_metadata::<Account>("balance", tag);
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "meta");
        var table = Assert.IsType<Table>(declaration.Initializer);
        var arg = Assert.IsType<TableInitializer>(Assert.Single(table.Initializers));
        var value = Assert.IsType<NumberLiteral>(arg.Value);
        Assert.Equal(2, value.Value);
    }

    [Fact]
    public void Generates_GetMetadata_FoldsToNilWhenAttributeAbsent()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn tag(): void { }
            interface Account {
                balance: number
            }
            let meta = get_metadata::<Account>("balance", tag);
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "meta");
        Assert.IsType<NilLiteral>(declaration.Initializer);
    }

    [Fact]
    public void Generates_HasAttribute_FoldsToBooleanLiteral()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn tag(): void { }
            interface Account {
                [tag]
                balance: number
                other: string
            }
            let present = has_attribute::<Account>("balance", tag);
            let absent = has_attribute::<Account>("other", tag);
            """,
            true
        );

        var present = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "present");
        var absent = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "absent");
        Assert.True(Assert.IsType<BooleanLiteral>(present.Initializer).Value);
        Assert.False(Assert.IsType<BooleanLiteral>(absent.Initializer).Value);
    }

    [Fact]
    public void Generates_GetMetadata_WithNoneMemberName_ReadsInterfaceLevelAttribute()
    {
        var luauTree = Utility.GetLuauAST(
            """
            enum Level { Low = 1, High = 2 }
            fn tag(level: Level): void { }
            [tag(Level::High)]
            interface Account { balance: number }
            let meta = get_metadata::<Account>(none, tag);
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "meta");
        var table = Assert.IsType<Table>(declaration.Initializer);
        var arg = Assert.IsType<TableInitializer>(Assert.Single(table.Initializers));
        Assert.Equal(2, Assert.IsType<NumberLiteral>(arg.Value).Value);
    }

    [Fact]
    public void Generates_GetMetadata_FoldsDoubleStringAndBoolArgs()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn tag(a: number, b: string, c: bool): void { }
            [tag(1.5, "hi", true)]
            interface Account { balance: number }
            let meta = get_metadata::<Account>(none, tag);
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "meta");
        var table = Assert.IsType<Table>(declaration.Initializer);
        Assert.Equal(3, table.Initializers.Count);

        var first = Assert.IsType<TableInitializer>(table.Initializers[0]);
        Assert.Equal(1.5, Assert.IsType<NumberLiteral>(first.Value).Value);

        var second = Assert.IsType<TableInitializer>(table.Initializers[1]);
        Assert.Equal("hi", Assert.IsType<StringLiteral>(second.Value).Value);

        var third = Assert.IsType<TableInitializer>(table.Initializers[2]);
        Assert.True(Assert.IsType<BooleanLiteral>(third.Value).Value);
    }

    [Fact]
    public void Generates_GetMetadata_FoldsNoneArgToNilLiteral()
    {
        var luauTree = Utility.GetLuauAST(
            """
            fn tag(a: unknown): void { }
            [tag(none)]
            interface Account { balance: number }
            let meta = get_metadata::<Account>(none, tag);
            """,
            true
        );

        var declaration = luauTree.Statements.OfType<ConstVariable>().Single(d => d.Name == "meta");
        var table = Assert.IsType<Table>(declaration.Initializer);
        var arg = Assert.IsType<TableInitializer>(Assert.Single(table.Initializers));
        Assert.IsType<NilLiteral>(arg.Value);
    }

    [Fact]
    public void ThrowsFor_GetMetadata_WithoutInterfaceTypeArgument()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            """
            fn tag(): void { }
            let meta = get_metadata::<number>("x", tag);
            """,
            true
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidTypeArguments, "'get_metadata' requires an interface type argument.");
    }

    [Fact]
    public void ThrowsFor_GetMetadata_WithWrongArgumentCount()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            """
            interface Account { balance: number }
            fn tag(): void { }
            let meta = get_metadata::<Account>("balance");
            """,
            true
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CompilerError, "'get_metadata' expects exactly 2 arguments.");
    }

    [Fact]
    public void ThrowsFor_GetMetadata_WithUnknownMemberName()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            """
            interface Account { balance: number }
            fn tag(): void { }
            let meta = get_metadata::<Account>("nonexistent", tag);
            """,
            true
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnknownMetadataMember, "Interface 'Account' has no member 'nonexistent'.");
    }

    [Fact]
    public void ThrowsFor_GetMetadata_WithNonStringNonNoneMemberName()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            """
            interface Account { balance: number }
            fn tag(): void { }
            let meta = get_metadata::<Account>(69, tag);
            """,
            true
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnknownMetadataMember, "'member_name' must be a string literal or 'none'.");
    }

    [Fact]
    public void ThrowsFor_GetMetadata_WithNonDecoratorAttributeReference()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            """
            interface Account { balance: number }
            let meta = get_metadata::<Account>("balance", 69);
            """,
            true
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidMetadataAttributeReference,
            "'attribute' must be a direct reference to a decorator function."
        );
    }

    [Fact]
    public void Generates_StringGlobal_FoldsStringLiteralArgument()
    {
        var luauTree = Utility.GetLuauAST("let m = string(\"hi\")");
        var declaration = Assert.IsType<ConstVariable>(luauTree.Statements.Single());
        Assert.Equal("hi", Assert.IsType<StringLiteral>(declaration.Initializer).Value);
    }

    [Fact]
    public void Generates_StringGlobal_FoldsBooleanLiteralArgument()
    {
        var luauTree = Utility.GetLuauAST("let m = string(true)");
        var declaration = Assert.IsType<ConstVariable>(luauTree.Statements.Single());
        Assert.Equal("true", Assert.IsType<StringLiteral>(declaration.Initializer).Value);
    }

    [Fact]
    public void Generates_StringGlobal_FoldsNoneLiteralArgument()
    {
        var luauTree = Utility.GetLuauAST("let m = string(none)");
        var declaration = Assert.IsType<ConstVariable>(luauTree.Statements.Single());
        Assert.Equal("nil", Assert.IsType<StringLiteral>(declaration.Initializer).Value);
    }

    [Fact]
    public void Generates_UndecoratedInterface_NoExtraStatements()
    {
        var luauTree = Utility.GetLuauAST(
            """
            interface Foo { x: number }
            let foo = new Foo { x: 1 };
            """,
            true
        );

        Assert.DoesNotContain(luauTree.Statements, s => s is ConstVariable { Name: "FooInfo" });
    }

    /// <remarks>
    ///     Luau's `and`/`or` short-circuit the expression, but not the statements a macro hoists to build one
    ///     of its operands - the instance filter macro lowers to a whole loop. Left ahead of the operator
    ///     those statements run whether or not the left side already decided the result, so the operator
    ///     promotes itself to a statement form and keeps them under the guard.
    /// </remarks>
    [Theory]
    [InlineData("&&", "if _and then")]
    [InlineData("||", "if not _or then")]
    public void Generates_ShortCircuit_GuardsTheRightOperandsHoistedStatements(string @operator, string guard)
    {
        var rendered = Utility.GenerateAgainstWorkspace($"let ok = true {@operator} world.get_children::<BasePart>().length > 0;");

        Assert.Contains(guard, rendered);
        Assert.Contains("  for _, child in _source do", rendered);
        Assert.DoesNotContain($" {(@operator == "&&" ? "and" : "or")} ", rendered);
    }

    [Fact]
    public void Generates_ShortCircuit_GuardsThemBehindANilCheckForNullCoalesce()
    {
        var rendered = Utility.GenerateAgainstWorkspace("let a: number? = 1; let b = a ?? world.get_children::<BasePart>().length;");

        Assert.Contains("local _coalesce = a", rendered);
        Assert.Contains("if _coalesce == nil then", rendered);
        Assert.Contains("  for _, child in _source do", rendered);
    }

    /// <remarks>
    ///     `&amp;&amp;=`/`||=`/`??=` desugar to `left = left &lt;op&gt; right`, so they inherit the same gap.
    /// </remarks>
    [Fact]
    public void Generates_ShortCircuit_GuardsThemForACompoundLogicalAssignment()
    {
        var rendered = Utility.GenerateAgainstWorkspace("mut ok = true; ok &&= world.get_children::<BasePart>().length > 0;");

        Assert.Contains("local _and = ok", rendered);
        Assert.Contains("if _and then", rendered);
        Assert.Contains("ok = _and", rendered);
    }

    /// <remarks>A right operand that hoists nothing has nothing to guard, so it keeps the plain operator.</remarks>
    [Theory]
    [InlineData("&&", " and ")]
    [InlineData("||", " or ")]
    public void Generates_ShortCircuit_PureRightOperandKeepsThePlainOperator(string @operator, string expected)
    {
        var rendered = Utility.GenerateAgainstWorkspace($"let ok = world.get_children::<BasePart>().length > 0 {@operator} true;");

        Assert.Contains(expected, rendered);
        Assert.DoesNotContain("local _and", rendered);
        Assert.DoesNotContain("local _or", rendered);
    }
}
