using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;

namespace Loom.Testing.Resolving;

[Collection("Assembly")]
public class NamespaceImportTest
{
    private const string MathModule = """
        export let pi: number = 3;
        export fn square(x: number): number -> x * x;
        export type Scalar = number;
        """;

    [Fact]
    public void Binds_TheWholeModule_ToOneLocalName() =>
        WithImportingModule(
            "import * as math from \"./math\"\nprint(math.pi);",
            (_, bindings) =>
            {
                var binding = Assert.Single(bindings);
                Assert.Equal("math", binding.LocalName);
                Assert.Equal("./math", binding.ModulePath);
                Assert.Equal("math.loom", binding.Module.Name);
                Assert.True(binding.IsUsed);
            }
        );

    [Fact]
    public void Types_TheNamespace_AsTheModulesRuntimeExports() =>
        WithImportingModule(
            "import * as math from \"./math\"\nprint(math.pi);",
            (result, bindings) =>
            {
                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
                var namespaceType = Assert.IsType<ObjectType>(main.SemanticModel.GetType(bindings[0].Import));

                // types are erased, so only the exports that exist at runtime are members
                Assert.Equal(["pi", "square"], namespaceType.Properties.Select(property => property.Name));
            }
        );

    [Fact]
    public void Includes_AnInternalMember_InTheNamespaceType_FromTheSameRoot() =>
        Utility.WithTempProject(
            [
                ("main.loom", "import * as math from \"./math\"\nprint(math::pi);"),
                ("math.loom", "export let pi: number = 3;\ninternal fn hash_key(k: number): number -> k;")
            ],
            (_, result) =>
            {
                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
                var binding = Assert.Single(main.SemanticModel.NamespaceImports);
                var namespaceType = Assert.IsType<ObjectType>(main.SemanticModel.GetType(binding.Import));
                Assert.Equal(["pi", "hash_key"], namespaceType.Properties.Select(property => property.Name));
            }
        );

    [Fact]
    public void TypeChecks_MemberAccess_ThroughTheNamespace() =>
        WithImportingModule(
            "import * as math from \"./math\"\nlet total: number = math::square(math::pi);\nprint(total);",
            (result, _) => Utility.AssertNoErrors(result)
        );

    [Fact]
    public void Reports_MemberAccess_ForANameTheModuleDoesNotExport() =>
        WithImportingModule(
            "import * as math from \"./math\"\nprint(math.cube);",
            (result, _) => Assert.Contains(
                result.Diagnostics.Set,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Message.Contains("cube")
            )
        );

    [Fact]
    public void Requires_TheModule_UnderTheNamespaceName() =>
        AssertGenerated(
            "import * as math from \"./math\"\nlet total = math::square(math::pi);\nprint(total);",
            """
            const math = require("./math")
            const total = math.square(math.pi)
            print(total)
            return nil
            """
        );

    [Fact]
    public void Shares_OneRequire_WithNamedImportsOfTheSameModule() =>
        AssertGenerated(
            "import * as math from \"./math\"\nimport { square } from \"./math\"\nprint(square(math::pi));",
            """
            const math = require("./math")
            const square = math.square
            print(square(math.pi))
            return nil
            """
        );

    [Fact]
    public void Warns_WhenTheNamespaceIsNeverUsed()
    {
        WithImportingModule(
            "import * as math from \"./math\"\nlet x = 1;\nprint(x);",
            assert
        );

        return;

        static void assert(CompilationResult result, List<NamespaceImportBinding> bindings)
        {
            Assert.Equal(0, bindings.Count(binding => binding.IsUsed));
            Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.UnusedImport,
                "'math' is imported but never used.",
                "remove the import"
            );
        }
    }

    [Fact]
    public void Reports_ANamespaceName_CollidingWithALocalDeclaration() =>
        WithImportingModule(
            "let math = 1;\nimport * as math from \"./math\"\nprint(math);",
            static (result, _) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.DuplicateName,
                "Variable 'math' is already declared in this scope."
            )
        );

    [Fact]
    public void Reports_ANamespaceImport_OutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel("fn f() { import * as math from \"./math\" }").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ImportOutsideModuleScope,
            "Modules can only be imported at the top level of a module.",
            "move the 'import' declaration out of the enclosing block"
        );
    }

    private static void WithImportingModule(string source, Action<CompilationResult, List<NamespaceImportBinding>> assert) =>
        Utility.WithTempProject(
            [("main.loom", source), ("math.loom", MathModule)],
            (_, result) =>
            {
                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
                assert(result, main.SemanticModel.NamespaceImports);
            }
        );

    private static void AssertGenerated(string source, string expected) =>
        Utility.WithTempProject(
            [("main.loom", source), ("math.loom", MathModule)],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
                Assert.Equal(
                    expected.Replace(Environment.NewLine, "\n") + '\n',
                    main.RenderedLuau.Replace(Environment.NewLine, "\n")
                );
            }
        );
}