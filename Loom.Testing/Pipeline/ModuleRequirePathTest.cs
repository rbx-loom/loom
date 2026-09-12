using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;

namespace Loom.Testing.Pipeline;

[Collection("Assembly")]
public class ModuleRequirePathTest
{
    private const string OutputMappedProject = """
        {
          "tree": {
            "$className": "DataModel",
            "ReplicatedStorage": {
              "Shared": { "$path": "dist" }
            }
          }
        }
        """;

    private const string OutputUnmappedProject = """
        {
          "tree": {
            "$className": "DataModel",
            "ReplicatedStorage": {
              "include": { "$path": "include" }
            }
          }
        }
        """;

    private static (string Path, string Source)[] GeometryAndMain =>
    [
        ("main.loom", "import { square } from \"./geometry\"\nprint(square(2));"), ("geometry.loom", "export fn square(x: number): number -> x * x;")
    ];

    [Fact]
    public void Requires_ByInstancePath_WhenRojoMapsTheOutput() =>
        AssertMainRequires(
            OutputMappedProject,
            """
            const geometry = require("@game/ReplicatedStorage/Shared/geometry")
            const square = geometry.square
            print(square(2))
            return nil
            """
        );

    [Fact]
    public void Requires_ADirectoryModule_ByItsFoldersInstancePath() =>
        Utility.WithTempProject(
            [("main.loom", "import { helper } from \"./util\"\nprint(helper);"), (Path.Combine("util", "init.loom"), "export let helper = 1;")],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Contains("require(\"@game/ReplicatedStorage/Shared/util\")", MainOutput(result));
            },
            OutputMappedProject
        );

    private static (string Path, string Source)[] UtilAndApp =>
    [
        ("app.loom", "import { helper } from \"./util\"\nprint(helper);"), (Path.Combine("util", "main.loom"), "export let helper = 1;")
    ];

    /// <remarks>
    ///     Unlike <c>init.lua</c>, Rojo does not fold a <c>main.lua</c> into its folder - the file lands as its
    ///     own <c>ModuleScript</c> named <c>main</c> inside the <c>util</c> folder, and the require path names
    ///     it exactly where Rojo actually put it.
    /// </remarks>
    [Fact]
    public void Requires_ADirectoryModule_ByItsMainFilesInstancePath() =>
        Utility.WithTempProject(
            UtilAndApp,
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Contains("require(\"@game/ReplicatedStorage/Shared/util/main\")", AppOutput(result));
            },
            OutputMappedProject
        );

    /// <remarks>
    ///     Luau's own require-by-string resolver folds a directory into its <c>init</c> file on its own, but
    ///     does not know Loom's <c>main.loom</c> convention - so a fallback naming <c>./util</c> outright would
    ///     resolve to nothing at runtime. The fallback has to name the file Luau will actually find.
    /// </remarks>
    [Fact]
    public void FallsBack_ToTheMainFilesOwnPath_WhenRojoDoesNotMapTheOutput() =>
        Utility.WithTempProject(
            UtilAndApp,
            (_, result) =>
            {
                Assert.Contains("require(\"./util/main\")", AppOutput(result));
                Utility.AssertDiagnostic(
                    result.Diagnostics,
                    InternalCodes.ModuleNotFoundInRojo,
                    "Could not locate module './util' through the Rojo project; falling back to a relative require.",
                    "add a $path mapping to your default.project.json that includes the output directory"
                );
            },
            OutputUnmappedProject
        );

    /// <remarks>A specifier naming the file directly already works unresolved - it is only the folder-fold that needs help.</remarks>
    [Fact]
    public void FallsBack_ToTheSpecifier_WhenItAlreadyNamesAMainFileDirectly() =>
        Utility.WithTempProject(
            [("app.loom", "import { helper } from \"./util/main\"\nprint(helper);"), (Path.Combine("util", "main.loom"), "export let helper = 1;")],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Contains("require(\"./util/main\")", AppOutput(result));
            }
        );

    [Fact]
    public void FallsBack_ToTheSpecifier_AndWarns_WhenRojoDoesNotMapTheOutput() =>
        Utility.WithTempProject(
            GeometryAndMain,
            (_, result) =>
            {
                Assert.Contains("require(\"./geometry\")", MainOutput(result));
                Utility.AssertDiagnostic(
                    result.Diagnostics,
                    InternalCodes.ModuleNotFoundInRojo,
                    "Could not locate module './geometry' through the Rojo project; falling back to a relative require.",
                    "add a $path mapping to your default.project.json that includes the output directory"
                );
            },
            OutputUnmappedProject
        );

    [Fact]
    public void FallsBack_Silently_WhenThereIsNoRojoProject() =>
        Utility.WithTempProject(
            GeometryAndMain,
            (_, result) =>
            {
                Assert.Contains("require(\"./geometry\")", MainOutput(result));

                // nothing to consult means nothing to warn about
                Assert.DoesNotContain(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.ModuleNotFoundInRojo);
            }
        );

    private static void AssertMainRequires(string rojoProject, string expected) =>
        Utility.WithTempProject(
            GeometryAndMain,
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Equal(
                    expected.Replace(Environment.NewLine, "\n") + '\n',
                    MainOutput(result).Replace(Environment.NewLine, "\n")
                );
            },
            rojoProject
        );

    private static string MainOutput(CompilationResult result) => result.Files.Single(file => file.SourceFile.Name == "main.loom").RenderedLuau;
    private static string AppOutput(CompilationResult result) => result.Files.Single(file => file.SourceFile.Name == "app.loom").RenderedLuau;
}