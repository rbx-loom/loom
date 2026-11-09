using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;


namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void ThrowsFor_Variable_DeclaredType_Mismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number = 'hello'");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hello\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_Assignment_DeclaredType_Mismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("mut x = 69; x = false");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'false' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_GenericTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type Id<T> = T; let x: Id<number> = 'hello'");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"hello\"' is not assignable to type 'Id<number>'.\n"
            + "    Type '\"hello\"' is not assignable to type 'number'."
        );
    }

    /// <remarks>
    ///     The resolver owns this error. Every later stage looks the name up and finds nothing, so each one
    ///     could report it again - the type checker as a failed symbol lookup, which reads like a compiler
    ///     bug rather than the misspelling it is.
    /// </remarks>
    [Fact]
    public void ThrowsFor_UndefinedIdentifier()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("x");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
        Utility.AssertReportedOnce(diagnostics, "x");
    }

    [Fact]
    public void ThrowsFor_UndefinedType()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("let x: A = 1");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find type 'A'.");
        Utility.AssertReportedOnce(diagnostics, "A");
    }

    [Fact]
    public void ThrowsFor_NonGeneric()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type A = number; let x: A<number> = 1");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.NotGeneric, "Type 'A' is not generic and cannot receive type arguments.");
    }

    [Fact]
    public void ThrowsFor_IncorrectGenericArity()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type A<T> = T; let x: A<number, bool> = 1");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.GenericArity, "Type 'A<T>' expects 1 type argument, but 2 were provided.");
    }

    [Fact]
    public void ThrowsFor_MismatchedDefaultType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type Id<T: number = \"abc\"> = T");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"abc\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_QualifiedName_InvalidTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo -> 42; foo.abc");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'abc' on type 'fn(): 42'.");
    }

    [Fact]
    public void ThrowsFor_PropertyAccess_InvalidTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("(69).abc");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'abc' on type '69'.");
    }

    [Fact]
    public void ThrowsFor_ElementAccess_InvalidTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo -> 42; foo[0]");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot index value of type 'fn(): 42'.");
    }

    [Fact]
    public void ThrowsFor_ElementAccess_InvalidIndex()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let arr: number[] = [1, 2, 3]; arr[true]");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type 'true' cannot be used to index type 'number[]'. Index is not of type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_ElementAccess_ImmutableAssignment()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let arr: number[] = [1, 2, 3]; arr[0] = 69;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable index 'number'.");
    }

    [Theory]
    [InlineData("type T = T")]
    [InlineData("type T = T | number")]
    [InlineData("type T = T & number")]
    [InlineData("type T = none | string & bool & T | number")]
    [InlineData("type X<T = T> = T")]
    [InlineData("type X<T: number = T> = T")]
    [InlineData("type X<T = T | number> = T")]
    [InlineData("type X<T = T & number> = T")]
    [InlineData("type X<T: string = T & number> = T")]
    [InlineData("type X<T = none | string & bool & T | number> = T")]
    [InlineData("type X<T: bool = none | string & bool & T | number> = T")]
    public void ThrowsFor_CircularTypeAlias_Reference(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InfiniteType, "Type 'T' circularly references itself.");
    }

    [Theory]
    [InlineData("type A = B; type B = A")]
    [InlineData("type A = B; type B = C; type C = A")]
    public void ThrowsFor_MutuallyCircularTypeAlias_Reference(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.Contains(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.InfiniteType);
    }

    [Theory]
    [InlineData("true in 1")]
    [InlineData("1 in true")]
    [InlineData("1 + true")]
    [InlineData("true + 1")]
    [InlineData("'abc' + 69")]
    [InlineData("'hello' - 'world'")]
    [InlineData("1 - 'hello'")]
    [InlineData("true * false")]
    [InlineData("1 / true")]
    [InlineData("1 // '2'")]
    [InlineData("5 % '3'")]
    [InlineData("1 ^ true")]
    [InlineData("1 & '2'")]
    [InlineData("1 | true")]
    [InlineData("1 << '2'")]
    [InlineData("1 >> true")]
    [InlineData("1 >>> '2'")]
    [InlineData("true && 5")]
    [InlineData("'hello' && false")]
    [InlineData("5 || 'world'")]
    [InlineData("true || 42")]
    public void ThrowsFor_BinaryOperator_InvalidOperandTypes(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.Contains(diagnostics.Set, d => d.Code is InternalCodes.TypeMismatch or InternalCodes.InvalidBinaryOp);
    }

    [Theory]
    [InlineData("!5")]
    [InlineData("!'hello'")]
    [InlineData("!42")]
    [InlineData("~true")]
    [InlineData("~'hello'")]
    [InlineData("-true")]
    [InlineData("-'hello'")]
    [InlineData("-false")]
    public void ThrowsFor_UnaryOperator_InvalidOperandType(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.Contains(diagnostics.Set, d => d.Code is InternalCodes.TypeMismatch or InternalCodes.InvalidUnaryOp);
    }

    [Fact]
    public void Checks_InOperator_StringKeyOnInterface() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("interface Foo { bar: string } let foo = new Foo { bar: \"abc\" }; \"bar\" in foo"));

    [Fact]
    public void Narrows_PropertyType_FromInOperator() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics("interface Foo { bar: number? } let foo = new Foo { bar: 1 }; if \"bar\" in foo foo.bar + 5")
        );

    [Fact]
    public void DoesNotNarrow_PropertyType_FromInOperator_WithNonLiteralKey()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Foo { bar: number? }
            let foo = new Foo { bar: 1 };
            let key = "bar";
            if key in foo {
                foo.bar + 5
            }
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Narrows_ParameterType_FromTypePredicate() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn is_number(value: unknown): value is number {
                    return true;
                }
                let v = 420 as unknown;
                if is_number(v) {
                    v + 69
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_TypePredicate_SubjectNotEnclosingParameter()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let outer: unknown = 1;
            fn f(x: unknown): outer is number {
                return true;
            }
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.InvalidTypePredicateSubject);
    }

    [Fact]
    public void Checks_SelfTypePredicate_OnPlainInterfaceMember() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface Widget { label: string }
                interface Container { is_kind: fn<T>(): @ is T }
                let c = none as never as Container;
                if c.is_kind::<Widget>() {
                    c.label
                }
                """
            )
        );

    [Fact]
    public void Narrows_ReceiverType_FromTraitMethodTypePredicate() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface Widget { label: string }
                trait HasKind<T> {
                    fn is_kind(): @ is T;
                }
                interface Container { name: string }
                implement HasKind<Widget> for Container {
                    fn is_kind() -> true;
                }
                let c = new Container { name: "box" };
                if c.is_kind() {
                    c.label
                }
                """
            )
        );

    [Fact]
    public void Checks_TypeOf_ReturnsString() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                let v = 69 as unknown;
                let kind: string = type_of(v);
                """
            )
        );

    [Fact]
    public void Narrows_ParameterType_FromTypeIs_BasePrimitive() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                let v = 69 as unknown;
                if type_is(v, "number") {
                    v + 1
                }
                """
            )
        );

    [Fact]
    public void Narrows_ParameterType_FromTypeIs_RobloxDataType() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                let v = none as never as unknown;
                if type_is(v, "Vector3") {
                    v.x
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_TypeIs_NonLiteralTypeNameArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let v = 69 as unknown;
            mut type_name = "number";
            if type_is(v, type_name) {
                v + 1
            }
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ThrowsFor_DeclareFunctionSignature_NonFunctionAttribute()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let not_a_function = 69;
            [not_a_function]
            declare fn foo(): void;
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.NonFunctionAttribute);
    }

    [Fact]
    public void ThrowsFor_NonGenericFunctionCall_ArgumentTypeMismatch()
    {
        const string source = """
            fn add(a: number, b: number) -> a + b
            add(1, "two")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"two\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericFunctionCall_InferenceConflict()
    {
        const string source = """
            fn first<T>(a: T, b: T) -> a
            first(42, "hello")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hello\"' is not assignable to type '42'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_AssignedToStructurallyIncompatibleType()
    {
        // 'new ReloadPacket { ... }' never checked its own bound type against 'expected' at all when
        // the interface being constructed isn't generic - it only validated the initializer against
        // ReloadPacket's own declared shape, so any two interfaces sharing zero property names were
        // silently interchangeable everywhere a value is checked against a context type.
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface ShootGunPacket { velocity: u8 }
            interface ReloadPacket { ammo: u8 }
            let wrong: ShootGunPacket = new ReloadPacket { ammo: 5 };
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'ReloadPacket' is not assignable to type 'ShootGunPacket'.\n"
            + "    Type '{ ammo: u8 }' is not assignable to type '{ velocity: u8 }'. Type '{ ammo: u8 }' is missing property 'velocity' required by type '{ velocity: u8 }'."
        );
    }

    [Fact]
    public void Allows_InterfaceInvocation_AssignedToItsOwnType() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface ShootGunPacket { velocity: u8 }
                let good: ShootGunPacket = new ShootGunPacket { velocity: 1 };
                """
            )
        );

    [Fact]
    public void ThrowsFor_NamedValue_AssignedToStructurallyIncompatibleType()
    {
        // The same two interfaces via a named value rather than 'new X {...}' directly - this path
        // (CheckSubsumption, not CheckInterfaceInvocation) already worked, but is worth pinning down
        // alongside the interface-invocation case since both went through IsAssignableTo correctly and
        // it was only TypeSolver's deferred-constraint path that had gaps.
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface ShootGunPacket { velocity: u8 }
            interface ReloadPacket { ammo: u8 }
            let reload = new ReloadPacket { ammo: 5 };
            let wrong: ShootGunPacket = reload;
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'ReloadPacket' is not assignable to type 'ShootGunPacket'.\n"
            + "    Type '{ ammo: u8 }' is not assignable to type '{ velocity: u8 }'. Type '{ ammo: u8 }' is missing property 'velocity' required by type '{ velocity: u8 }'."
        );
    }

    [Fact]
    public void ThrowsFor_FunctionArgument_StructurallyIncompatibleInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface ShootGunPacket { velocity: u8 }
            interface ReloadPacket { ammo: u8 }
            fn take(x: ShootGunPacket): void { }
            take(new ReloadPacket { ammo: 5 });
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'ReloadPacket' is not assignable to type 'ShootGunPacket'.\n"
            + "    Type '{ ammo: u8 }' is not assignable to type '{ velocity: u8 }'. Type '{ ammo: u8 }' is missing property 'velocity' required by type '{ velocity: u8 }'."
        );
    }

    [Fact]
    public void Allows_ForLoop_OverRange() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                mut result = 1;
                for i : 2..5
                    result *= i;
                """
            )
        );

    [Fact]
    public void ThrowsFor_FunctionCall_IncorrectGenericArity()
    {
        const string source = """
            fn id<T>(value: T) -> value
            id::<number, string>(69)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.GenericArity,
            "Function expects 1 type argument, but 2 were provided."
        );
    }

    [Fact]
    public void ThrowsFor_FunctionCall_IncorrectArity()
    {
        const string source = """
            fn id(value: number) -> value
            id(69, 420)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvocationArity,
            "Function expects 1 argument, but 2 were provided.",
            "remove 1 argument"
        );
    }

    [Fact]
    public void ThrowsFor_FunctionCall_WithOptionalParams_IncorrectArity()
    {
        const string source = """
            fn id(value: number, other: number?) -> value
            id(69, 420, 1337)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvocationArity,
            "Function expects 1-2 arguments, but 3 were provided.",
            "remove 1 argument"
        );
    }

    [Fact]
    public void ThrowsFor_FunctionCall_TooFewArguments_HintsHowManyMore()
    {
        const string source = """
            fn add(a: number, b: number, c: number) -> a + b + c
            add(1)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvocationArity,
            "Function expects 3 arguments, but 1 were provided.",
            "pass 2 more arguments"
        );
    }

    [Fact]
    public void Checks_NamedArgument_SkipsMiddleDefault() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn move_to(target: number, speed: number = 16, smooth: bool = false): void { }
                move_to(target: 1, smooth: true);
                """
            )
        );

    [Fact]
    public void Checks_NamedArgument_MixedWithPositional() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn move_to(target: number, speed: number = 16, smooth: bool = false): void { }
                move_to(1, smooth: true);
                """
            )
        );

    [Fact]
    public void ThrowsFor_NamedArgument_WrongType()
    {
        const string source = """
            fn id(value: number): void { }
            id(value: "nope");
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"nope\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_NamedArgument_UnknownParameterName()
    {
        const string source = """
            fn id(value: number): void { }
            id(nonexistent: 1);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnknownArgumentName, "'nonexistent' is not a parameter of this function.");
    }

    [Fact]
    public void ThrowsFor_NamedArgument_MissingRequiredParameter()
    {
        const string source = """
            fn move_to(target: number, speed: number = 16): void { }
            move_to(speed: 1);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.MissingRequiredArgument, "Missing required argument for parameter 'target'.");
    }

    [Fact]
    public void ThrowsFor_NamedArgument_AlreadySpecifiedPositionally()
    {
        const string source = """
            fn move_to(target: number, speed: number = 16): void { }
            move_to(1, target: 2);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ArgumentSpecifiedMultipleTimes, "Parameter 'target' is already specified.");
    }

    [Fact]
    public void ThrowsFor_NamedArgument_OnFunctionWithUnknownDeclaration()
    {
        const string source = """
            let f = fn(value: number): void { };
            f(value: 1);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NamedArgumentUnknownDeclaration,
            "Named arguments can only be used to call a function whose declaration is statically known."
        );
    }

    [Fact]
    public void ThrowsFor_NamedArgument_OnOverloadedInvocation()
    {
        const string source = """
            declare interface Shape { x: number; y: number; }
            declare interface ShapeStatic {
                create: fn(): Shape;
                create: fn(x: number, y: number): Shape;
            }
            declare let Shape: ShapeStatic;

            Shape.create(x: 1, y: 2)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NamedArgumentWithOverload,
            "Named arguments cannot be used when calling an overloaded function."
        );
    }

    [Fact]
    public void Checks_NamedArgument_InfersGenericTypeParameter()
    {
        const string source = """
            fn wrap<T>(value: T, label: string = "x"): T -> value
            wrap(label: "y", value: 5)
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        Assert.True(Utility.GetLastStatementType(source).IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_NamedArgument_OnEventFire() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics("event abc(a: number, b: string); abc(b: \"x\", a: 10);")
        );

    [Fact]
    public void ThrowsFor_NamedArgument_MissingRequiredEventParameter()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("event abc(a: number, b: string); abc(b: \"x\");");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.MissingRequiredArgument, "Missing required argument for parameter 'a'.");
    }

    [Fact]
    public void ThrowsFor_GenericFunctionCall_ExplicitTypeArgumentMismatch()
    {
        const string source = """
            fn id<T>(value: T) -> value
            id::<string>(69)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '69' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericFunctionCall_WithConstraintViolation()
    {
        const string source = """
            fn identity<T: number>(value: T) -> value
            identity("hello")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type '\"hello\"' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericTypeAlias_WithConstraintViolation()
    {
        const string source = """
            type Box<T: number> = T
            let x: Box<string> = "hello"
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'string' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericTypeAlias_MissingRequiredTypeParameter()
    {
        const string source = """
            type Id<T, U = number> = T
            type X = Id
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.GenericArity, "Type 'Id<T, U = number>' expects 1-2 type arguments, but 0 were provided.");
    }

    [Fact]
    public void ThrowsFor_NonNumeric_RangeLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("'a'..'b'");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"a\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_QualifiedName_Chained_AfterNumber()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let r = (1..10); r.minimum.foo");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'foo' on type 'number'.");
    }

    [Fact]
    public void ThrowsFor_QualifiedName_Chained_MissingIntermediate()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let r = (1..10); r.missing.next");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"missing\"' cannot be used to index type 'Range'. Property 'missing' does not exist on type 'Range'."
        );
    }

    [Fact]
    public void ThrowsFor_PropertyAccess_Chained_AfterNumber()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("(1..10).minimum.foo");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'foo' on type 'number'.");
    }

    [Fact]
    public void ThrowsFor_PropertyAccess_Chained_MissingIntermediate()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("(1..10).missing.next");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"missing\"' cannot be used to index type 'Range'. Property 'missing' does not exist on type 'Range'."
        );
    }

    [Fact]
    public void ThrowsFor_StringEnumMemberWithoutInitializer()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Colors : string { Red, Green = \"00FF00\" }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StringEnumMemberMustHaveInitializer,
            "Member 'Red' of string enum 'Colors' must have an initializer."
        );
    }

    [Fact]
    public void ThrowsFor_InvalidEnumBaseType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Flags : bool { Flag1 = true }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidEnumBaseType,
            "Invalid enum base type.",
            "valid types are 'string' and 'number'"
        );
    }

    [Fact]
    public void ThrowsFor_EnumMemberNonConstantInitializer()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = 3; enum Test { A = x, B }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.DynamicEnumMemberInitializer,
            "Enum member initializers must be constant values."
        );
    }

    [Theory]
    [InlineData("enum Test { A = 1 + 2 }", 3.0)]
    [InlineData("enum Test { A = 1 << 0 }", 1.0)]
    [InlineData("enum Test { A = 1 << 2 }", 4.0)]
    [InlineData("enum Test { A = 0b0001 | 0b0010 }", 3.0)]
    [InlineData("enum Test { A = 0b0110 & 0b0011 }", 2.0)]
    [InlineData("enum Test { A = -5 }", -5.0)]
    public void Checks_EnumMember_FoldsArithmeticAndBitwiseConstantInitializer(string source, double expectedValue)
    {
        var type = Utility.GetLastStatementType(source);
        var objectType = Assert.IsType<ObjectType>(type);
        var property = objectType.GetProperty("A");
        var literalType = Assert.IsType<LiteralType>(property!.ValueType);
        Assert.Equal(expectedValue, literalType.Value);
    }

    /// <summary>Every arithmetic and bitwise operator an enum member's initializer can fold at compile time, not just the shift <see cref="Checks_BitwiseOr_OnSameEnumMembers_PreservesEnumType" /> already exercises.</summary>
    [Fact]
    public void Checks_EnumMemberInitializer_FoldsEveryConstantOperator() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                enum Ops {
                    Sub = 5 - 2,
                    Mul = 3 * 2,
                    Div = 7 / 2,
                    FloorDiv = 7 // 2,
                    Pow = 2 ^ 3,
                    Mod = 7 % 3,
                    Xor = 5 ~ 3,
                    UnsignedShift = 16 >>> 2,
                }
                """
            )
        );

    [Fact]
    public void Checks_BitwiseOr_OnSameEnumMembers_PreservesEnumType() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                enum Flags { A = 1 << 0, B = 1 << 1, C = 1 << 2 }
                fn accept(flags: Flags): void { }
                accept(Flags::A | Flags::B)
                """
            )
        );

    /// <summary>GetEnumMemberOwner's ElementAccess arm - the bitwise-union special case has to recognize an enum operand reached through bracket indexing ('Flags["A"]'), not just '::'.</summary>
    [Fact]
    public void Checks_BitwiseOr_OnSameEnumMembers_ViaElementAccess_PreservesEnumType() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                enum Flags { A = 1 << 0, B = 1 << 1, C = 1 << 2 }
                fn accept(flags: Flags): void { }
                accept(Flags["A"] | Flags["B"])
                """
            )
        );

    [Fact]
    public void ThrowsFor_BitwiseOr_OnDifferentEnums_WidensToNumber()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            enum Flags { A = 1, B = 2 }
            enum Other { X = 1, Y = 2 }
            fn accept(flags: Flags): void { }
            accept(Flags::A | Other::X)
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'number' is not assignable to type '1 | 2'.");
    }

    [Fact]
    public void ThrowsFor_EnumTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Status { Active, Inactive } let x: Status = 5");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '5' is not assignable to type '0 | 1'.");
    }

    [Fact]
    public void Allows_ReservedLuauKeywordAsEnumMemberName()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Test { until, A }");
        Assert.Null(diagnostics.Find(d => d.Code == InternalCodes.ReservedLuauKeyword));
    }

    [Fact]
    public void Allows_ReservedLuauKeywordAsStringEnumMemberName()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Test: string { until = \"until\" }");
        Assert.Null(diagnostics.Find(d => d.Code == InternalCodes.ReservedLuauKeyword));
    }

    [Fact]
    public void ThrowsFor_IfStatement_NonBooleanCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("if 42 { 1 }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '42' is not assignable to type 'bool'."
        );
    }

    [Fact]
    public void ThrowsFor_IfStatement_WithOptionalCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: bool? = true; if x { 1 }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'bool?' is not assignable to type 'bool'."
        );
    }

    [Fact]
    public void ThrowsFor_Never_InBinaryOperation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = none; if x != none x + 1");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidBinaryOp, "No binary operation for 'never' + 'number'.");
    }

    [Fact]
    public void ThrowsFor_Never_InUnaryOperation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = none; if x != none { -x }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidUnaryOp, "No unary operation for -never.");
    }

    [Fact]
    public void ThrowsFor_InvalidCall()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = 1; x()");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidInvocation, "Cannot call value of type '1'.");
    }

    [Fact]
    public void ThrowsFor_Cast_Mismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("69 as string");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '69' is not assignable to type 'string'.");
    }

    [Fact]
    public void Checks_NullForgiving_StripsOptionality()
    {
        var type = Utility.GetLastStatementType("let x: number? = 5; let y = x!;");
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_NullForgiving_OnInterfaceProperty_StripsOptionality()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Foo { bar: number? }
            let foo = new Foo { bar: 1 };
            foo.bar!
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void ThrowsFor_NullForgiving_RedundantWhenNotOptional()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number = 5; let y = x!;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.RedundantCode, "Null-forgiving operator has no effect since 'number' is not optional.");
    }

    [Fact]
    public void ThrowsFor_AssignToImmutable_ObjectIndexer()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            "interface ImmutRecord<K, V> { [K]: V }; let x = none as never as ImmutRecord<string, bool>; x['abc'] = false"
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable index 'string'.");
    }

    [Fact]
    public void ThrowsFor_AssignToImmutable_ObjectProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface Obj { prop: number }; let x = none as never as Obj; x.prop = 69");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable property 'prop'.");
    }

    [Fact]
    public void ThrowsFor_AssignToImmutable_Nested_ObjectProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            "interface Inner { prop: number } interface Obj { inner: Inner }; let x = none as never as Obj; x.inner.prop = 69"
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable property 'prop'.");
    }

    /// <summary>A dotted assignment target whose receiver isn't a bare identifier (a call result here) parses as PropertyAccess rather than QualifiedName - CheckImmutableAssignmentTarget has to route through that arm too, not just QualifiedName's.</summary>
    [Fact]
    public void ThrowsFor_AssignToImmutable_ThroughPropertyAccess()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 { x: number, y: number }
            fn make(): Vector2 -> new Vector2 { x: 0, y: 0 };
            make().x = 5;
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable property 'x'.");
    }

    /// <summary>The assignment target's receiver ('n') isn't a NativelyIndexableType at all (a bare number), so CheckImmutableAssignmentTarget must bail out before ever walking a property chain.</summary>
    [Fact]
    public void ThrowsFor_PropertyAssignment_OnNonIndexableReceiver()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let n: number = 5; n.something = 5;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'something' on type 'number'.");
    }

    /// <summary>Unlike <see cref="ThrowsFor_AssignToImmutable_Nested_ObjectProperty" />, the intermediate step of the chain ('inner') isn't itself a NativelyIndexableType - CheckImmutableAssignmentTarget must bail out of the walk rather than assume every intermediate property is an object.</summary>
    [Fact]
    public void ThrowsFor_NestedPropertyAssignment_ThroughNonIndexableIntermediate()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Outer { inner: number }
            let o: Outer = new Outer { inner: 5 };
            o.inner.something = 5;
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'something' on type 'number'.");
    }

    [Theory]
    [InlineData("let s = \"abcdef\"; s[1..3] = \"abc\"", "string[Range]")]
    [InlineData("let s = \"abcdef\"; s[1] = \"a\"", "string[number]")]
    [InlineData("let a = mut [1, 2, 3]; a[1..2] = [69, 420]", "number[mut][Range]")]
    public void ThrowsFor_AccessMacro_Assignment(string source, string assignType)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, $"Cannot assign to '{assignType}' because the expression will be replaced by a macro.");
    }

    [Fact]
    public void ThrowsFor_IndexedType_InvalidTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type X = number['abc']");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Type '\"abc\"' cannot be used to index type 'number'.");
    }

    [Fact]
    public void ThrowsFor_ParameterDefaultMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo(a: number = 'oops') {}");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"oops\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_NotAnInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = 1; new x {}");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidInvocation, "Type '1' is not an interface.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_PropertyNotFound()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { x: number } new I { foo: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Property 'foo' does not exist on interface 'I'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_PropertyTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { x: number } new I { x: 'abc' }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"abc\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_IndexerRequired()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { } new I { [0]: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Interface 'I' does not have an indexer.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_IndexerTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { [number]: string } new I { ['text']: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"text\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_MissingPropertyInitializer()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { x: number, y: number } new I { x: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.IncompleteInterfaceInvocation, "Missing property initializer for 'y' in interface 'I'.");
    }

    [Fact]
    public void Checks_WithOperator_ResultTypeMatchesLeftOperand()
    {
        const string source = "interface I { x: number, y: string } let i = new I { x: 1, y: 'a' }; i with { x: 2 }";
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal("I", interfaceType.Name);
    }

    [Fact]
    public void Allows_WithOperator_OmittingFields() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics("interface I { x: number, y: string } let i = new I { x: 1, y: 'a' }; i with { x: 2 }")
        );

    [Fact]
    public void Allows_WithOperator_ShorthandField() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics("interface I { x: number, y: string } let i = new I { x: 1, y: 'a' }; let x = 2; i with { x }")
        );

    [Fact]
    public void Allows_WithOperator_IndexInitializer() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                "interface I { [string]: bool } let i = new I { ['a']: true }; i with { ['b']: false }"
            )
        );

    [Fact]
    public void ThrowsFor_WithOperator_NotAnInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = 1; x with { x: 2 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidWithOperand, "'with' requires an interface value, got '1'.");
    }

    [Fact]
    public void ThrowsFor_WithOperator_PropertyNotFound()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { x: number } let i = new I { x: 1 }; i with { foo: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Property 'foo' does not exist on interface 'I'.");
    }

    [Fact]
    public void ThrowsFor_WithOperator_PropertyTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { x: number } let i = new I { x: 1 }; i with { x: 'abc' }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"abc\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_GenericWrongArity()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I<T> { value: T } new I::<number, string> { value: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.GenericArity, "Interface 'I<T>' expects 1 type argument, but 2 were provided.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_GenericConstraintViolation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I<T: number> { value: T } new I::<string> { value: 'x' }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintViolation, "Type 'string' does not satisfy constraint 'number' for type parameter 'T'.");
    }

    [Fact]
    public void ThrowsFor_WhileLoop_NonBooleanCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("while 1 { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '1' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_After_NonNumberDuration()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("after true { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'true' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_Every_NonNumberDuration()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("every true { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'true' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_Every_NonBooleanCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("every 1s while 1 { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '1' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_FunctionTypeParameterDefault_ConstraintViolation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo<T: number = 'hello'>() {}");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hello\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_IndexedType_MissingProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum E { A, B } type X = E[\"nonexistent\"]");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"nonexistent\"' cannot be used to index type '{ A: 0, B: 1 }'. Property 'nonexistent' does not exist on type '{ A: 0, B: 1 }'."
        );
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_NonBoolCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("1 ? 2 : 3");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '1' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_OptionalBoolConditionWithoutNarrowing()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let b: bool? = true; b ? 1 : 2");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'bool?' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_ConditionIsString()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("'hi' ? 1 : 2");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hi\"' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_ConditionIsNumber()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("42 ? 1 : 2");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '42' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_ConditionIsOptionalStringWithoutNarrowing()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let s: string? = 'a'; s ? 1 : 2");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'string?' is not assignable to type 'bool'.");
    }

    private const string MergedMapping = """
        enum Message { ShootGun, Reload }

        interface ShootGunPacket { velocity: number }
        interface ReloadPacket { ammo: number }

        declare interface ShootGunEntry { [Message["ShootGun"]]: ShootGunPacket; }
        declare interface ReloadEntry { [Message["Reload"]]: ReloadPacket; }
        declare interface MessageData: ShootGunEntry, ReloadEntry;

        """;

    [Fact]
    public void Checks_KeyOf_UnionsEveryInheritedIndexersKey()
    {
        var type = Utility.GetLastStatementType($"{MergedMapping}declare let key: keyof(MessageData);\nkey");

        Assert.Equal("0 | 1", type.ToString());
    }

    /// <remarks>
    ///     Properties and indexers both reach through a whole chain of inheritance, so keys have to as well -
    ///     they used to stop one level short, leaving 'keyof' over a grandparent's property empty.
    /// </remarks>
    [Fact]
    public void Checks_KeyOf_ReachesThroughAMultiLevelInheritanceChain()
    {
        var type = Utility.GetLastStatementType("interface A { x: number }\ninterface B: A { }\ninterface C: B { }\ndeclare let key: keyof(C);\nkey");

        Assert.Equal("\"x\"", type.ToString());
    }

    [Fact]
    public void Checks_IndexingByAUnionOfKeys_UnionsTheValuesTheyReach()
    {
        var type = Utility.GetLastStatementType($"{MergedMapping}declare let value: MessageData[keyof(MessageData)];\nvalue");

        Assert.Equal("ShootGunPacket | ReloadPacket", type.ToString());
    }

    /// <remarks>
    ///     Asserted by what the key is <em>not</em> assignable to: the key used to come back as 'never', which
    ///     goes into anything, so a loop that accepted every narrower type was the shape of the bug.
    /// </remarks>
    [Fact]
    public void Checks_ForOverAMergedMapping_BindsEveryInheritedKey()
    {
        const string source = $$"""
            {{MergedMapping}}declare let data: MessageData;
            declare fn take_any(key: keyof(MessageData)): void;
            declare fn take_one(key: Message["ShootGun"]): void;

            for key, value : data {
                take_any(key);
                take_one(key);
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);

        Assert.Single(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '0 | 1' is not assignable to type '0'.");
    }

    /// <inheritdoc cref="Checks_ForOverAMergedMapping_BindsEveryInheritedKey" />
    [Fact]
    public void Checks_ForOverAMergedMapping_BindsEveryInheritedValue()
    {
        const string source = $$"""
            {{MergedMapping}}declare let data: MessageData;
            declare fn take_any(packet: ShootGunPacket | ReloadPacket): void;
            declare fn take_one(packet: ShootGunPacket): void;

            for key, value : data {
                take_any(value);
                take_one(value);
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);

        Assert.Single(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'ShootGunPacket | ReloadPacket' is not assignable to type 'ShootGunPacket'."
        );
    }

    /// <remarks>
    ///     A union index may turn out to be any one of its members, so a write through it is only sound when
    ///     every indexer it reaches is mutable.
    /// </remarks>
    [Fact]
    public void ThrowsFor_WritingThroughAUnionIndex_WhereOneIndexerIsImmutable()
    {
        const string source = """
            enum M { A, B }
            interface P { v: number }
            declare interface ReadEntry { [M["A"]]: P; }
            declare interface WriteEntry { mut [M["B"]]: P; }
            declare interface Mixed: ReadEntry, WriteEntry;
            declare let mixed: Mixed;
            declare let key: keyof(Mixed);
            declare let packet: P;
            mixed[key] = packet;
            """;

        Utility.AssertDiagnostic(Utility.GetTypeCheckerDiagnostics(source), InternalCodes.AssignToImmutable, "Cannot assign to immutable index '0 | 1'.");
    }

    [Fact]
    public void Checks_WritingThroughAUnionIndex_WhereEveryIndexerIsMutable() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                enum M { A, B }
                interface P { v: number }
                declare interface FirstEntry { mut [M["A"]]: P; }
                declare interface SecondEntry { mut [M["B"]]: P; }
                declare interface Both: FirstEntry, SecondEntry;
                declare let both: Both;
                declare let key: keyof(Both);
                declare let packet: P;
                both[key] = packet;
                """
            )
        );

    /// <remarks>
    ///     A type parameter in target position used to be rejected outright, leaving the declared return type
    ///     as 'never' - assignable to whatever the caller annotated, so every such call silently type-checked.
    /// </remarks>
    [Fact]
    public void Checks_IndexedReturnType_ThroughATypeParameterTarget()
    {
        const string declaration = "interface Named { a: number, b: string }\ndeclare fn pick<T, K>(key: K): T[K];\n";

        Assert.Equal("number", Utility.GetLastStatementType($"{declaration}pick::<Named, \"a\">(\"a\")").ToString());
        Assert.Equal("ShootGunPacket", Utility.GetLastStatementType($"{MergedMapping}{declaration}pick::<MessageData, Message[\"ShootGun\"]>(Message::ShootGun)").ToString());
        Assert.Equal(
            "ShootGunPacket | ReloadPacket",
            Utility.GetLastStatementType($"{MergedMapping}{declaration}pick::<MessageData, keyof(MessageData)>(Message::ShootGun)").ToString()
        );
    }

    /// <remarks>
    ///     A substituted index that is still a type parameter leaves 'T[K]' unresolved, so it keeps naming the
    ///     value that goes with that key rather than collapsing to whichever indexer answered first.
    /// </remarks>
    [Fact]
    public void Checks_IndexedReturnType_StaysDeferred_WhileTheIndexIsStillAParameter()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            $$"""
              {{MergedMapping}}declare fn pick<T, K>(key: K): T[K];
              fn get_packet<K: Message>(message: K): MessageData[K] -> pick::<MessageData, K>(message);
              """
        );

        Utility.AssertNoErrors(diagnostics);
    }



    [Fact]
    public void ThrowsFor_KeyOf_OnPrimitive()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type K = keyof(number)");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidKeyOf, "Cannot access keys of type 'number'.");
    }

    [Fact]
    public void ThrowsFor_KeyOf_OnFunctionType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type K = keyof(fn(): void)");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidKeyOf, "Cannot access keys of type 'fn(): void'.");
    }

    [Fact]
    public void ThrowsFor_NestedKeyOf()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { a: number } type K = keyof(keyof(I))");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidKeyOf, "Cannot access keys of type 'string'.");
    }

    [Fact]
    public void ThrowsFor_GenericInterfaceWithConstraintViolation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I<T: number> { value: T }; new I::<string> { value: 'x' }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintViolation, "Type 'string' does not satisfy constraint 'number' for type parameter 'T'.");
    }

    [Fact]
    public void ThrowsFor_ConstraintViolation_DeepInstantiation()
    {
        const string source = """
            type Box<T> = T
            type NumericBox<T: Box<number>> = T
            let x: NumericBox<string> = "hello"
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintViolation, "Type 'string' does not satisfy constraint 'Box<number>' for type parameter 'T'.");
    }

    [Fact]
    public void ThrowsFor_GenericTypeAlias_UnionArgumentViolatesConstraint()
    {
        const string source = """
            type OnlyNum<T: number> = T
            let x: OnlyNum<number | string> = 1
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'number | string' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericFunctionCall_InferredUnionViolatesConstraint()
    {
        const string source = """
            fn onlyNum<T: number>(x: T) -> x
            onlyNum(42 as (number | string))
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'number | string' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_Inference_VariableDeclarationAnnotation_ViolatesConstraint()
    {
        const string source = """
            fn create<T: number>(value: T) -> value
            let x: string = create()
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'string' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_Inference_ArgumentBasedBinding_TakesPriorityOverConflictingContext()
    {
        const string source = """
            fn wrap<T>(value: T) -> value
            let x: string = wrap(42)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '42' is not assignable to type 'string'.");
    }

    [Fact]
    public void Infers_GenericFunctionArgument_FromExpectedParameterType()
    {
        const string source = """
            let numbers = [1, 2, 3, 4];
            fn map<T, U>(array: T[], converter: fn(e: T): U): U[] {
              let new_array: U[mut] = mut [];
              for v : array
                new_array.push(converter(v));

              return new_array;
            }

            fn id<T>(n: T) -> n;
            map(numbers, id);
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Infers_GenericFunctionArgument_ResultTypeIsPrecise()
    {
        const string source = """
            let numbers = [1, 2, 3, 4];
            fn map<T, U>(array: T[], converter: fn(e: T): U): U[] {
              let new_array: U[mut] = mut [];
              for v : array
                new_array.push(converter(v));

              return new_array;
            }

            fn id<T>(n: T) -> n;
            let result: number[] = map(numbers, id);
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_GenericFunctionArgument_InferredTypeViolatesConstraint()
    {
        const string source = """
            fn map<T, U>(array: T[], converter: fn(e: T): U): U[] {
              let new_array: U[mut] = mut [];
              for v : array
                new_array.push(converter(v));

              return new_array;
            }

            fn identity<T: string>(x: T) -> x;
            let numbers = [1, 2, 3];
            map(numbers, identity);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'fn<T: string>(T: string): T: string' is not assignable to type 'fn(number): number'."
        );
    }

    [Fact]
    public void Allows_GenericFunction_UsedAsPlainValue_NotAsArgument()
    {
        const string source = """
            fn id<T>(n: T) -> n;
            print(id::<number>(5));
            print(id(5));
            let f = id;
            print(f::<string>("hi"));
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_Indexer_Override()
    {
        const string source = """
            interface Def {
                [string]: number;
            }

            interface Abc: Def {
                [number]: string;
            }

            let abc = new Abc { };
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintIndexerOverride, "An indexer is already declared within constraint 'Def'.");
    }

    [Fact]
    public void ThrowsFor_Property_Override()
    {
        const string source = """
            interface Def {
                abc: number;
            }

            interface Abc: Def {
                abc: string;
            }

            let abc = new Abc { abc: 69 };
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintPropertyOverride, "Property 'abc' is already declared within constraint 'Def'.");
    }

    [Theory]
    [InlineData(": string", "string")]
    [InlineData("", "\"\"")]
    public void ThrowsFor_Implement_ReturnTypeMismatch(string explicitReturn, string expected)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            $$"""
            trait Iterator {
                fn next(): number
            }

            interface Foo;

            implement Iterator for Foo {
                fn next(){{explicitReturn}} {
                    return ""
                }
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            $"Type '{expected}' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_Implement_ParameterTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            trait Add {
                fn add(x: number): void
            }

            interface Foo;

            implement Add for Foo {
                fn add(x: string) { }
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'string' is not assignable to type 'number'."
        );
    }

    private const string Vector2WithStaticCreate = """
        interface Vector2 {
            x: number
            y: number
            static create: fn(x: number, y: number): Vector2
        }

        static Vector2 {
            fn create(x, y) { return new Vector2 { x, y }; }
        }
        """;

    [Fact]
    public void Allows_StaticAccess_WithColonColon() =>
        Utility.AssertNoErrors(Utility.TypeCheck($"{Vector2WithStaticCreate}\nlet v: Vector2 = Vector2::create(1, 2);"));

    [Fact]
    public void ThrowsFor_StaticMember_AccessedWithDot()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics($"{Vector2WithStaticCreate}\nlet v: Vector2 = Vector2.create(1, 2);");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.WrongOperatorForMemberKind,
            "'create' is a static member of 'Vector2' - '.' cannot access it.",
            "use '::create' instead"
        );
    }

    [Fact]
    public void ThrowsFor_InstanceMember_AccessedWithColonColon()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 { x: number }
            let v: Vector2 = new Vector2 { x: 1 };
            v::x;
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.WrongOperatorForMemberKind,
            "'x' is an instance member of 'Vector2' - '::' cannot access it.",
            "use '.x' instead"
        );
    }

    [Fact]
    public void ThrowsFor_InstanceMember_AccessedThroughInterfaceName()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics($"{Vector2WithStaticCreate}\nlet x = Vector2.x;");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InstanceMemberViaInterfaceName,
            "'x' is an instance member of 'Vector2' - 'Vector2' names the interface itself, not a value of it.",
            "construct one first, e.g. 'new Vector2 { ... }'"
        );
    }

    [Fact]
    public void Allows_ResultOk_ViaColonColon() => Utility.AssertNoErrors(Utility.TypeCheck("let x: Result<number, string> = Result::ok(69);"));

    [Fact]
    public void Allows_ResultErr_ViaColonColon() => Utility.AssertNoErrors(Utility.TypeCheck("let x: Result<number, string> = Result::err(\"msg\");"));

    [Fact]
    public void ThrowsFor_ResultOk_AccessedWithDot()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = Result.ok(69);");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.WrongOperatorForMemberKind,
            "'ok' is a static member of 'Result' - '.' cannot access it.",
            "use '::ok' instead"
        );
    }

    [Fact]
    public void ThrowsFor_ResultErr_AccessedWithDot()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = Result.err(\"msg\");");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.WrongOperatorForMemberKind,
            "'err' is a static member of 'Result' - '.' cannot access it.",
            "use '::err' instead"
        );
    }

    [Fact]
    public void Allows_EnumMemberAccess_WithColonColon() =>
        Utility.AssertNoErrors(Utility.TypeCheck("enum Status { Active, Inactive }\nlet x: Status = Status::Active;"));

    [Fact]
    public void ThrowsFor_EnumMember_AccessedWithDot()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Status { Active, Inactive }\nlet x: Status = Status.Active;");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.WrongOperatorForMemberKind,
            "'Active' is a static member of '{ Active: 0, Inactive: 1 }' - '.' cannot access it.",
            "use '::Active' instead"
        );
    }

    [Fact]
    public void Allows_StaticAccess_OnGenericInterface() =>
        Utility.AssertNoErrors(
            Utility.TypeCheck(
                """
                interface Box<T: number = f32> {
                    x: T
                    static zero: Box
                }

                static Box {
                    zero = new Box { x: 0 };
                }

                let b = Box::zero;
                let x = b.x;
                """
            )
        );

    /// <summary>Bug #231-1: a generic static block was entirely unchecked - neither missing nor mistyped members were ever reported.</summary>
    [Fact]
    public void ThrowsFor_StaticBlock_MissingMember_OnGenericInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box<T = number> {
                value: T
                static empty: Box
                static wrap: fn(v: T): Box
            }

            static Box {
                fn wrap(v) { return new Box { value: v }; }
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticBlockMissingMember,
            "Static block for interface 'Box' is missing member 'empty'."
        );
    }

    /// <summary>Bug #231-7 (return-type half): a generic static method's body was never checked against its declared signature.</summary>
    [Fact]
    public void ThrowsFor_StaticBlockMethod_ReturnTypeMismatch_OnGenericInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box<T = number> {
                value: T
                static wrap: fn(v: T): Box
            }

            static Box {
                fn wrap(v) -> "not a Box";
            }
            """
        );

        Assert.Contains(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.TypeMismatch);
    }

    /// <summary>Bug #231-2: TypeChecker.Generics.cs's SubstituteObjectType rebuilt every property without forwarding IsStatic, silently demoting statics to instance fields under generic substitution.</summary>
    [Fact]
    public void Allows_InterfaceInvocation_OmittingStaticMembers_OnGenericInterface() =>
        Utility.AssertNoErrors(
            Utility.TypeCheck(
                """
                interface Box<T = number> {
                    x: T
                    static make: fn(x: number): Box
                }

                let b = new Box { x: 1 };
                """
            )
        );

    /// <summary>Bug #231-3: an object-literal field name resolving to a static member corrupted the emitted per-instance table with no diagnostic.</summary>
    [Fact]
    public void ThrowsFor_StaticMember_InObjectLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 {
                x: number
                y: number
                static zero: Vector2
            }

            static Vector2 { zero = new Vector2 { x: 0, y: 0 }; }

            let v = new Vector2 { x: 1, y: 2, zero: Vector2::zero };
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticMemberInObjectLiteral,
            "'zero' is a static member of 'Vector2' - it cannot be set on an instance literal."
        );
    }

    /// <summary>Bug #231-6: a static member reached through a real instance via '::' resolved cleanly since only the reverse direction (instance member via the interface name) was checked.</summary>
    [Fact]
    public void ThrowsFor_StaticMember_AccessedThroughInstance()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 {
                x: number
                y: number
                static zero: Vector2
            }

            static Vector2 { zero = new Vector2 { x: 0, y: 0 }; }

            let v = new Vector2 { x: 1, y: 2 };
            let z = v::zero;
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticMemberViaInstance,
            "'zero' is a static member of 'Vector2' - it is not reachable through an instance of 'Vector2'."
        );
    }

    /// <summary>Bug #231-8: bracket access built no dotKind/receiver-kind arguments at all, so it bypassed both the operator-kind and receiver-kind checks entirely.</summary>
    [Fact]
    public void ThrowsFor_StaticMember_AccessedThroughInstance_WithBrackets()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 {
                x: number
                y: number
                static zero: Vector2
            }

            static Vector2 { zero = new Vector2 { x: 0, y: 0 }; }

            let v = new Vector2 { x: 1, y: 2 };
            let z = v["zero"];
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticMemberViaInstance,
            "'zero' is a static member of 'Vector2' - it is not reachable through an instance of 'Vector2'."
        );
    }

    /// <summary>Bracket access through the interface's own namespace value is still valid - only a real instance is rejected.</summary>
    [Fact]
    public void Allows_StaticAccess_WithBrackets_ThroughInterfaceName() =>
        Utility.AssertNoErrors(
            Utility.TypeCheck(
                """
                interface Vector2 {
                    x: number
                    y: number
                    static zero: Vector2
                }

                static Vector2 { zero = new Vector2 { x: 0, y: 0 }; }

                let z = Vector2["zero"];
                """
            )
        );

    /// <summary>Bug #231-9: TypeSimplifier.ResolveIndex, used for an intersection-typed receiver, took no dotKind/receiver-kind parameters at all, so 'A &amp; B' bypassed the check entirely.</summary>
    [Fact]
    public void ThrowsFor_StaticMember_AccessedThroughIntersectionInstance()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface A { static shared: number }
            interface B { static shared: number }

            static A { shared = 1; }
            static B { shared = 2; }

            fn use_it(v: A & B) {
                v.shared;
            }
            """
        );

        Assert.Contains(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.InvalidAccess);
    }

    /// <summary>
    ///     Bug #231-1: the Enum migration merged an instance member ('Material: EnumItem&lt;"Material"&gt;')
    ///     and a static member ('static Material: EnumMaterial') into the same 'declare interface Enum'
    ///     under the identical name - InterfaceType.EnsureCaches' first-wins TryAdd then permanently shadowed
    ///     the static half, hard-failing the single most common Roblox enum-access pattern. Enum alone was
    ///     reverted to the original two-interface (EnumStatic/Enum) shape; every other migrated intrinsic
    ///     stays on 'static'.
    /// </summary>
    [Fact]
    public void Allows_Enum_InstanceAndStaticAccess() =>
        Utility.AssertNoErrors(Utility.TypeCheck("let m = Enum.Material.Plastic;"));

    [Fact]
    public void Allows_InterfaceInvocation_OmittingStaticMembers() =>
        Utility.AssertNoErrors(
            Utility.TypeCheck(
                """
                interface Vector2 {
                    x: number
                    y: number
                    static zero: Vector2
                }

                static Vector2 { zero = new Vector2 { x: 0, y: 0 }; }
                """
            )
        );

    [Fact]
    public void ThrowsFor_StaticBlockField_TypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 {
                static zero: number
            }

            static Vector2 { zero = "hello"; }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hello\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_StaticBlock_MissingMember()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 {
                static zero: Vector2
                static one: Vector2
                x: number
                y: number
            }

            static Vector2 { zero = new Vector2 { x: 0, y: 0 }; }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticBlockMissingMember,
            "Static block for interface 'Vector2' is missing member 'one'."
        );
    }

    [Fact]
    public void ThrowsFor_StaticBlock_RepeatedField()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 {
                static zero: Vector2
                x: number
            }

            static Vector2 {
                zero = new Vector2 { x: 0 };
                zero = new Vector2 { x: 1 };
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.DuplicateStaticMember,
            "Static member 'zero' is already defined in this block.",
            "remove one of them"
        );
    }

    [Fact]
    public void ThrowsFor_StaticBlock_ExtraMember()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 {
                static zero: Vector2
                x: number
                y: number
            }

            static Vector2 {
                zero = new Vector2 { x: 0, y: 0 };
                one = new Vector2 { x: 1, y: 1 };
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticBlockExtraMember,
            "Interface 'Vector2' does not declare a static member 'one'."
        );
    }

    [Fact]
    public void ThrowsFor_StaticBlock_ExtraMethod()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector2 {
                x: number
                y: number
                static zero: Vector2
            }

            static Vector2 {
                zero = new Vector2 { x: 0, y: 0 };
                fn extra() {}
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticBlockExtraMember,
            "Interface 'Vector2' does not declare a static member 'extra'."
        );
    }

    /// <summary>A generic interface's own name with no defaulted parameters comes back as a bare GenericType rather than an InstantiatedType - ExpandBareGenericType fills the missing argument with 'unknown' so the static block body can still be checked.</summary>
    [Fact]
    public void ThrowsFor_StaticBlock_OnBareGenericInterface_MissingTypeArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box<T> {
                x: T
                static zero: Box
            }

            static Box {
                zero = new Box { x: 0 };
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.GenericArity, "Type 'Box<T>' expects 1 type argument, but 0 were provided.");
    }

    /// <summary>The resolver already rejects a non-interface static-block target, so the type checker never binds a reference for it - VisitStaticBlock's own name lookup then fails too and it binds the block to 'never' rather than crashing.</summary>
    [Fact]
    public void ThrowsFor_StaticBlock_OnNonInterfaceType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            type Alias = number

            static Alias {
                fn foo() {}
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindSymbol, "Cannot find symbol for declaration of type 'Alias'.");
    }

    /// <summary>Exercises the full static-method parameter pipeline at once: a default value on the implementation's own parameter, an explicit colon type on that same parameter, an optional declared parameter type narrowed by the default, and an explicit return type annotation on the method.</summary>
    [Fact]
    public void Allows_StaticMethod_WithDefaultedOptionalParameter_AndExplicitReturnType() =>
        Utility.AssertNoErrors(
            Utility.TypeCheck(
                """
                interface Vector2 {
                    x: number
                    y: number
                    static make: fn(x: number, y: number?): Vector2
                }

                static Vector2 {
                    fn make(x: number, y: number = 0): Vector2 -> new Vector2 { x, y };
                }

                let v = Vector2::make(1);
                """
            )
        );

    [Fact]
    public void Allows_NamespaceAccess_WithColonColon() =>
        Utility.WithTempProject(
            [
                ("main.loom", "import * as math from \"./math\"\nlet total: number = math::square(math::pi);\nprint(total);"),
                ("math.loom", "export let pi: number = 3;\nexport fn square(x: number): number -> x * x;")
            ],
            (_, result) => Utility.AssertNoErrors(result)
        );

    [Fact]
    public void ThrowsFor_NamespaceAccess_WithDot() =>
        Utility.WithTempProject(
            [
                ("main.loom", "import * as math from \"./math\"\nlet total: number = math.square(math.pi);\nprint(total);"),
                ("math.loom", "export let pi: number = 3;\nexport fn square(x: number): number -> x * x;")
            ],
            (_, result) => Assert.Contains(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.WrongOperatorForMemberKind)
        );

    [Fact]
    public void Checks_SelfExpression_IndexerAccess_MatchesTraitReturnType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface WithIndexer { [string]: number }

            trait GetValue<K, V> {
                fn get_value(key: K): V
            }

            implement GetValue<string, number> for WithIndexer {
                fn get_value(key) -> @[key];
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_SelfExpression_TypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            trait GetSelf {
                fn get_self(): number
            }

            interface Foo;

            implement GetSelf for Foo {
                fn get_self() -> @;
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'Foo' is not assignable to type 'number'.");
    }

    [Fact]
    public void Checks_SelfExpression_SeesMethodsFromOtherImplementedTraits()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
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
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_SelfExpression_SeesDefaultedMethodFromOtherImplementedTrait()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Container { value: number }
            trait Greeting { fn greet(): string -> "hi"; }
            trait Announcer { fn announce(): void }

            implement Greeting for Container { }
            implement Announcer for Container {
                fn announce -> print(@.greet());
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_ConstructedValue_HasDefaultedTraitMethod()
    {
        const string source = """
            trait Greeting { fn greet(): string -> "hi"; }
            interface Container { }
            implement Greeting for Container { }
            new Container { }.greet()
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        Assert.True(Utility.GetLastStatementType(source).IsAssignableTo(PrimitiveType.String));
    }

    [Fact]
    public void Checks_DefaultTraitMethod_SelfExpressionTypesAsUnknown()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("trait Foo { fn a(): void -> print(@); }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_DefaultTraitMethod_SelfExpressionMemberAccess()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            trait Foo {
                fn bar(): void;
                fn baz(): void -> @.bar();
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'bar' on type 'unknown'.");
    }

    [Fact]
    public void ThrowsFor_Implement_DefaultValueWrongType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            trait Add {
                fn add(x: number): void
            }

            interface Foo;

            implement Add for Foo {
                fn add(x = "") { }
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_UnconstrainedTypeParameter_Index()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn abc<T>(i: T) -> ([1, 2, 3])[i]");

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type 'T' cannot be used to index type 'number[]'. Type parameter 'T' is unconstrained."
        );
    }
}
