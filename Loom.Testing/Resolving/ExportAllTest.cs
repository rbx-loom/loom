using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;

namespace Loom.Testing.Resolving;

[Collection("Assembly")]
public class ExportAllTest
{
    private const string GeometryModule = """
        export let pi = 3;
        export type Scalar = number;
        export interface Point { x: number y: number }
        """;

    [Fact]
    public void ReExports_EveryNameOfTheModule() =>
        WithModule(
            "export * from \"./geometry\"",
            (result, exports) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Equal(["pi", "Scalar", "Point"], exports.Select(export => export.Name).Distinct());
                Assert.All(exports, export => Assert.Equal("geometry.loom", export.Module?.Name));
                Assert.All(exports, export => Assert.Equal(export.Name, export.SourceName));
            }
        );

    [Fact]
    public void ReExports_UnderTheNameTheModulePublishes() =>
        Utility.WithTempProject(
            [
                ("geometry.loom", GeometryModule),
                ("aliases.loom", "export { pi as TAU } from \"./geometry\""),
                ("index.loom", "export * from \"./aliases\"")
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                var export = Assert.Single(Exports(result, "index.loom"));
                Assert.Equal("TAU", export.Name);
                Assert.Equal("TAU", export.SourceName); // read off the starred module's table, not the declaration's name
                Assert.Equal("aliases.loom", export.Module?.Name);
            }
        );

    [Fact]
    public void ReExports_OnlyTypes_WhenTypeOnly() =>
        WithModule(
            "export type * from \"./geometry\"",
            (result, exports) =>
            {
                Utility.AssertNoErrors(result);
                Assert.All(exports, export => Assert.True(export.Symbol.IsTypeSymbol));
                Assert.Equal(["Scalar", "Point"], exports.Select(export => export.Name));
            }
        );

    [Theory]
    [InlineData("export let pi = 4;\nexport * from \"./geometry\"")]
    [InlineData("export * from \"./geometry\"\nexport let pi = 4;")]
    public void PrefersTheFilesOwnExport_WhicheverComesFirst(string source) =>
        WithModule(
            source,
            (result, exports) =>
            {
                Utility.AssertNoErrors(result);

                var pi = Assert.Single(exports, export => export.Name == "pi");
                Assert.Null(pi.Module);
                Assert.Equal("index.loom", pi.Symbol.File.Name);
            }
        );

    [Fact]
    public void Generates_ARequire_AndForwardsThroughIt() =>
        Utility.WithTempProject(
            [("geometry.loom", GeometryModule), ("index.loom", "export * from \"./geometry\"")],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                AssertRendered(
                    result,
                    "index.loom",
                    """
                    const geometry = require("./geometry")
                    export type Scalar = geometry.Scalar
                    export type Point = geometry.Point
                    return { pi = geometry.pi }
                    """
                );
            }
        );

    /// <summary>An interface carries no runtime binding, so only its codec makes it into the table.</summary>
    [Fact]
    public void Forwards_TheCodecOfASerializableInterface() =>
        Utility.WithTempProject(
            [
                ("geometry.loom", "[serializable]\nexport interface Point { x: number y: number }"),
                ("index.loom", "export * from \"./geometry\"")
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                AssertRendered(
                    result,
                    "index.loom",
                    """
                    const geometry = require("./geometry")
                    export type Point = geometry.Point
                    return { Point_serializer = geometry.Point_serializer }
                    """
                );
            }
        );

    [Fact]
    public void Imports_ThroughAStarReExportingModule() =>
        Utility.WithTempProject(
            [
                ("geometry.loom", GeometryModule),
                ("index.loom", "export * from \"./geometry\""),
                ("main.loom", "import { pi, Point } from \"./index\"\nlet p: Point = new Point { x: pi, y: pi };\nprint(p);")
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                // the require goes through the module that was imported from, not the original declaration's
                AssertRendered(
                    result,
                    "main.loom",
                    """
                    const index = require("./index")
                    const pi = index.pi
                    type Point = index.Point
                    const p: Point = { x = pi, y = pi }
                    print(p)
                    return nil
                    """
                );
            }
        );

    [Fact]
    public void ForwardsThroughAChainOfStars() =>
        Utility.WithTempProject(
            [
                ("geometry.loom", GeometryModule),
                ("shapes.loom", "export * from \"./geometry\""),
                ("index.loom", "export * from \"./shapes\""),
                ("main.loom", "import { pi } from \"./index\"\nprint(pi);")
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                var export = Assert.Single(Exports(result, "index.loom"), binding => binding.Name == "pi");
                Assert.Equal("shapes.loom", export.Module?.Name); // the module it was starred from, not where it was declared

                AssertRendered(
                    result,
                    "index.loom",
                    """
                    const shapes = require("./shapes")
                    export type Scalar = shapes.Scalar
                    export type Point = shapes.Point
                    return { pi = shapes.pi }
                    """
                );
            }
        );

    /// <summary>Two stars reaching one declaration name the same thing, so neither is ambiguous.</summary>
    [Fact]
    public void Allows_TwoStarsForwardingTheSameDeclaration() =>
        Utility.WithTempProject(
            [
                ("geometry.loom", GeometryModule),
                ("left.loom", "export * from \"./geometry\""),
                ("right.loom", "export * from \"./geometry\""),
                ("index.loom", "export * from \"./left\"\nexport * from \"./right\"")
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                var export = Assert.Single(Exports(result, "index.loom"), binding => binding.Name == "pi");
                Assert.Equal("left.loom", export.Module?.Name);
            }
        );

    [Fact]
    public void Reports_TwoStarsExportingTheSameName() =>
        Utility.WithTempProject(
            [
                ("geometry.loom", GeometryModule),
                ("other.loom", "export let pi = 4;"),
                ("index.loom", "export * from \"./geometry\"\nexport * from \"./other\"")
            ],
            (_, result) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.AmbiguousStarExport,
                "'pi' is exported by both './geometry' and './other'.",
                "name it in an 'export { pi } from' to pick the one you meant"
            )
        );

    [Fact]
    public void Reports_TheSameModuleStarredTwice() =>
        WithModule(
            "export * from \"./geometry\"\nexport * from \"./geometry\"",
            (result, _) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.DuplicateExport,
                "Everything from './geometry' is already re-exported."
            )
        );

    /// <summary>A type-only star adds nothing to a module already starred whole.</summary>
    [Fact]
    public void Reports_ATypeOnlyStarOfAnAlreadyStarredModule() =>
        WithModule(
            "export * from \"./geometry\"\nexport type * from \"./geometry\"",
            (result, _) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.DuplicateExport,
                "Everything from './geometry' is already re-exported."
            )
        );

    [Fact]
    public void Widens_ATypeOnlyStar_WithALaterWholeStar() =>
        WithModule(
            "export type * from \"./geometry\"\nexport * from \"./geometry\"",
            (result, exports) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Equal(["Scalar", "Point", "pi"], exports.Select(export => export.Name).Distinct());
            }
        );

    [Fact]
    public void Reports_AStarOfAModuleThatDoesNotExist() =>
        WithModule(
            "export * from \"./nope\"",
            (result, exports) =>
            {
                Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.ModuleNotFound, "Could not find module './nope'.");
                Assert.Empty(exports);
            }
        );

    [Fact]
    public void Reports_AStarOutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel("fn f() { export * from \"./geometry\" }").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ExportOutsideModuleScope,
            "Declarations can only be exported at the top level of a module.",
            "move the 'export' declaration out of the enclosing block"
        );
    }

    private static List<ExportBinding> Exports(CompilationResult result, string fileName) =>
        result.Files.Single(file => file.SourceFile.Name == fileName).SemanticModel.Exports;

    private static void WithModule(string source, Action<CompilationResult, List<ExportBinding>> assert) =>
        Utility.WithTempProject(
            [("index.loom", source), ("geometry.loom", GeometryModule)],
            (_, result) => assert(result, Exports(result, "index.loom"))
        );

    private static void AssertRendered(CompilationResult result, string fileName, string expected)
    {
        var file = result.Files.Single(compiled => compiled.SourceFile.Name == fileName);
        Assert.Equal(
            expected.Replace(Environment.NewLine, "\n") + '\n',
            file.RenderedLuau.Replace(Environment.NewLine, "\n")
        );
    }
}
