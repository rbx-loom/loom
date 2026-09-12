using Loom.Core.Pipeline;

namespace Loom.Testing.Generation;

[Collection("Assembly")]
public class ImportGeneratorTest
{
    private const string GeometryModule = """
        export let pi = 3;
        export fn square(x: number): number -> x * x;
        export type Scalar = number;
        export type Box<T> = T[];
        export enum Color { Red = 1, Blue = 2 }
        export interface Point { x: number y: number }
        """;

    [Fact]
    public void Requires_TheModule_AndBindsEachValue() =>
        AssertGenerated(
            "import { square, pi } from \"./geometry\"\nprint(square(pi));",
            """
            const geometry = require("./geometry")
            const square = geometry.square
            const pi = geometry.pi
            print(square(pi))
            return nil
            """
        );

    [Fact]
    public void Binds_AnAlias_ToItsExportedName() =>
        AssertGenerated(
            "import { pi as PI } from \"./geometry\"\nprint(PI);",
            """
            const geometry = require("./geometry")
            const PI = geometry.pi
            print(PI)
            return nil
            """
        );

    [Fact]
    public void Aliases_ImportedTypes_OntoTheRequiredModule() =>
        AssertGenerated(
            "import type { Scalar } from \"./geometry\"\nlet total: Scalar = 1;\nprint(total);",
            """
            const geometry = require("./geometry")
            type Scalar = geometry.Scalar
            const total: Scalar = 1
            print(total)
            return nil
            """
        );

    [Fact]
    public void Aliases_GenericImportedTypes_WithTheirParameters() =>
        AssertGenerated(
            "import type { Box } from \"./geometry\"\nlet items: Box<number> = mut [1];\nprint(items);",
            """
            const geometry = require("./geometry")
            type Box<T> = geometry.Box<T>
            const items: Box<number> = {1}
            print(items)
            return nil
            """
        );

    [Fact]
    public void Aliases_ImportedEnum_WithoutBindingAValue() =>
        AssertGenerated(
            "import { Color } from \"./geometry\"\nlet c: Color = Color::Blue;\nprint(c);",
            """
            const geometry = require("./geometry")
            type Color = geometry.Color
            const c: Color = 2
            print(c)
            return nil
            """
        );

    [Fact]
    public void Aliases_ImportedInterface_UsedAsBothTypeAndValue() =>
        AssertGenerated(
            "import { Point } from \"./geometry\"\nlet p: Point = new Point { x: 1, y: 2 };\nprint(p.x);",
            """
            const geometry = require("./geometry")
            type Point = geometry.Point
            const p: Point = { x = 1, y = 2 }
            print(p.x)
            return nil
            """
        );

    [Fact]
    public void Requires_AModule_OnceForEveryImportDeclarationNamingIt() =>
        AssertGenerated(
            """
            import { pi } from "./geometry"
            import type { Scalar } from "./geometry"
            let total: Scalar = pi;
            print(total);
            """,
            """
            const geometry = require("./geometry")
            const pi = geometry.pi
            type Scalar = geometry.Scalar
            const total: Scalar = pi
            print(total)
            return nil
            """
        );

    [Fact]
    public void Suffixes_TheModuleLocal_WhenTheNameIsAlreadyDeclared() =>
        AssertGenerated(
            "import { pi } from \"./geometry\"\nlet geometry = 1;\nprint(geometry, pi);",
            """
            const geometry_1 = require("./geometry")
            const pi = geometry_1.pi
            const geometry = 1
            print(geometry, pi)
            return nil
            """
        );

    [Fact]
    public void Requires_TheRuntimeLibrary_BeforeAnyModule() =>
        AssertGenerated(
            "import { pi } from \"./geometry\"\nevent tick;\nprint(pi);",
            """
            const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
            const geometry = require("./geometry")
            const pi = geometry.pi
            const tick: Loom.Event = Loom.Event.new()
            print(pi)
            return nil
            """
        );

    [Fact]
    public void Requires_ADirectoryModule_ByItsFolderName() =>
        Utility.WithTempProject(
            [("main.loom", "import { helper } from \"./util\"\nprint(helper);"), (Path.Combine("util", "init.loom"), "export let helper = 2;")],
            (_, result) => AssertRendered(
                result,
                """
                const util = require("./util")
                const helper = util.helper
                print(helper)
                return nil
                """
            )
        );

    /// <remarks>
    ///     The last segment names the module, which is what a reader is looking for; when something else has
    ///     already taken that name, the segment above it says which module this is far better than a number.
    /// </remarks>
    [Fact]
    public void Qualifies_AModuleLocal_WithTheSegmentAboveIt_WhenItsOwnNameIsTaken() =>
        Utility.WithTempProject(
            [
                ("main.loom", "import { zero } from \"./util/vector\"\nlet vector = 1;\nprint(zero, vector);"),
                (Path.Combine("util", "vector.loom"), "export let zero = 0;")
            ],
            (_, result) => AssertRendered(
                result,
                """
                const util_vector = require("./util/vector")
                const zero = util_vector.zero
                const vector = 1
                print(zero, vector)
                return nil
                """
            )
        );

    private static void AssertGenerated(string source, string expected) =>
        Utility.WithTempProject(
            [("main.loom", source), ("geometry.loom", GeometryModule)],
            (_, result) => AssertRendered(result, expected)
        );

    private static void AssertRendered(CompilationResult result, string expected)
    {
        Utility.AssertNoErrors(result);

        var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
        Assert.Equal(
            expected.Replace(Environment.NewLine, "\n") + '\n',
            main.RenderedLuau.Replace(Environment.NewLine, "\n")
        );
    }
}
