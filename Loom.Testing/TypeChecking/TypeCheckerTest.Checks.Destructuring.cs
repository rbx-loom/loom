using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;


namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_ArrayDestructuring_BindsElementType()
    {
        var type = Utility.GetLastStatementType("let array: number[] = [1, 2, 3]; let [first, second] = array; first;");
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_ObjectDestructuring_BindsPropertyType()
    {
        var type = Utility.GetLastStatementType(
            """
            interface User { name: string, age: number }
            let user = new User { name: "Ada", age: 30 };
            let { name, age } = user;
            age;
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_ObjectDestructuring_WithAlias_BindsAliasToPropertyType()
    {
        var type = Utility.GetLastStatementType(
            """
            interface User { age: number }
            let user = new User { age: 30 };
            let { age: userAge } = user;
            userAge;
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void ThrowsFor_ObjectDestructuring_UnknownProperty()
    {
        const string source = """
            interface User { name: string }
            let user = new User { name: "Ada" };
            let { age } = user;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnknownDestructureProperty, "Property 'age' does not exist on type 'User'.");
    }

    [Fact]
    public void ThrowsFor_ArrayDestructuring_OnNonArraySource()
    {
        const string source = """
            let n: number = 1;
            let [a, b] = n;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDestructureSource, "Cannot destructure value of type 'number' with an array pattern.");
    }

    [Fact]
    public void Checks_NestedObjectDestructuring_BindsLeafToInnerPropertyType()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Address { city: string }
            interface User { name: string, address: Address }
            let user = new User { name: "a", address: new Address { city: "b" } };
            let { address: { city } } = user;
            city;
            """
        );

        Assert.Equal(PrimitiveType.String, type);
    }

    [Fact]
    public void Checks_ArrayNestedInsideObjectDestructuring_BindsElementType()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Summary { scores: number[] }
            let summary = new Summary { scores: [10, 20] };
            let { scores: [first, second] } = summary;
            second;
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_ObjectNestedInsideArrayDestructuring_BindsPropertyType()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Point { x: number, y: number }
            let points = [new Point { x: 1, y: 2 }];
            let [{ x: firstX }] = points;
            firstX;
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void ThrowsFor_NestedObjectDestructuring_UnknownProperty()
    {
        const string source = """
            interface Address { city: string }
            interface User { name: string, address: Address }
            let user = new User { name: "a", address: new Address { city: "b" } };
            let { address: { country } } = user;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnknownDestructureProperty, "Property 'country' does not exist on type 'Address'.");
    }

    [Fact]
    public void Checks_ArrayDestructuringElement_WithDefault_BindsElementType()
    {
        var type = Utility.GetLastStatementType(
            """
            let maybe_pair: number[] = [1];
            let [first, second = 0] = maybe_pair;
            second;
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_ObjectDestructuringField_WithDefault_BindsPropertyTypeWithoutNone()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Config { retries: number? }
            let config = new Config { retries: none };
            let { retries = 3 } = config;
            retries;
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void ThrowsFor_ArrayDestructuringDefault_NotAssignableToElementType()
    {
        const string source = """
            let numbers: number[] = [1];
            let [first = "not a number"] = numbers;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"not a number\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_ObjectDestructuringDefault_NotAssignableToPropertyType()
    {
        const string source = """
            interface Config { retries: number? }
            let config = new Config { retries: none };
            let { retries = "not a number" } = config;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"not a number\"' is not assignable to type 'number?'.");
    }

    [Fact]
    public void Checks_NestedDestructuring_DefaultedOptionalField_ThenDestructuresTheRemainder()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Address { city: string }
            interface User { address: Address? }
            fn run(user: User, fallback: Address) {
                let { address: { city } = fallback } = user;
                print(city);
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_ObjectDestructuring_OfAnOptionalSource()
    {
        const string source = """
            interface User { name: string }
            fn run(maybe: User?) {
                let { name } = maybe;
                print(name);
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.PossiblyNoneAccess, "'User?' is possibly 'none'.");
    }

    [Fact]
    public void ThrowsFor_NestedDestructuring_ThroughAnOptionalField()
    {
        const string source = """
            interface Address { city: string }
            interface User { address: Address? }
            fn run(user: User) {
                let { address: { city } } = user;
                print(city);
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.PossiblyNoneAccess, "'Address?' is possibly 'none'.");
    }

    [Fact]
    public void ThrowsFor_ArrayDestructuring_OfAnOptionalSource()
    {
        const string source = """
            fn run(maybe: number[]?) {
                let [first] = maybe;
                print(first);
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.PossiblyNoneAccess, "'number[]?' is possibly 'none'.");
    }

    [Fact]
    public void Checks_ObjectDestructuring_OfANarrowedOptionalSource_IsAccepted()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface User { name: string }
            fn run(maybe: User?) {
                if maybe != none {
                    let { name } = maybe;
                    print(name);
                }
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_NamedArgument_TypePredicate_NarrowsTheArgumentNamedForTheParameter()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn is_text(a: unknown, b: unknown): b is string {
                return match b {
                    s when string -> true,
                    _ -> false,
                };
            }

            fn run(x: unknown, y: unknown) {
                if is_text(b: x, a: y) {
                    let s: string = x;
                    print(s);
                }
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }
}
