using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Luau.AST;
using BinaryOperator = Loom.Core.Parsing.AST.BinaryOperator;
using ExpressionStatement = Loom.Core.Parsing.AST.ExpressionStatement;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;

namespace Loom.Testing.Pipeline;

[Collection("Assembly")]
public partial class CompilationUnitTest
{
    [Fact]
    public void Compiles_Project_NoEmit()
    {
        var config = GetConfig();
        config.NoEmit = true;

        var compilationUnit = new CompilationUnit(config);
        var result = compilationUnit.Compile();
        Utility.AssertNoErrors(result);
        Assert.Single(result.Files);

        var path = config.Files.OutputDirectory;
        Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        File.Create(Path.Combine(path, ".gitkeep")).Dispose();

        var luauFiles = Directory.EnumerateFiles(path, "*.luau", SearchOption.TopDirectoryOnly);
        Assert.Empty(luauFiles);
    }

    [Fact]
    public void Compiles_Project()
    {
        var config = GetConfig();
        var compilationUnit = new CompilationUnit(config);
        var result = compilationUnit.Compile();
        Utility.AssertNoErrors(result);
        Assert.Single(result.Files);

        var file = result.Files.Find(file => file.Path.EndsWith("basic_binary.luau"));
        Assert.NotNull(file);
        Assert.Equal(4, file.Tokens.Count);
        Assert.Single(file.Tree.Statements);
        Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(file.Tree.Statements.First()).Expression);
        Assert.Null(file.SemanticModel.GetSymbol(file.Tree));
        Assert.Equal(PrimitiveType.Number, file.ReturnType);
        Assert.Equal(2, file.LuauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(file.LuauTree.Statements[0]);
        var binary = Assert.IsType<Luau.AST.BinaryOperator>(variable.Initializer);
        Assert.Equal("_", variable.Name);
        Assert.IsType<NumberLiteral>(binary.Left);
        Assert.IsType<NumberLiteral>(binary.Right);

        var returnStatement = Assert.IsType<Return>(file.LuauTree.Statements[1]);
        Assert.IsType<NilLiteral>(returnStatement.Expression);

        var path = config.Files.OutputDirectory;
        Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        File.Create(Path.Combine(path, ".gitkeep")).Dispose();
    }

    [Fact]
    public void Compiles_Project_WithDeclarationFile_PopulatesGlobals()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        var srcDir = Path.Combine(dir, "src");
        Directory.CreateDirectory(srcDir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "loom-config.toml"),
                "project_type = \"game\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );

            File.WriteAllText(Path.Combine(srcDir, "types.d.loom"), "declare let global_number: number;");
            File.WriteAllText(Path.Combine(srcDir, "main.loom"), "let x = 1;");

            var config = ConfigReader.LocateFromDirectory(dir);
            Assert.NotNull(config);
            config.NoEmit = true;

            var compilationUnit = new CompilationUnit(config);
            var result = compilationUnit.Compile();

            Utility.AssertNoErrors(result);
            Assert.Equal(2, result.Files.Count);
            var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
            Assert.Contains(compilationUnit.Globals.Of(main.SourceFile).Keys, symbol => symbol.Name == "global_number");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <remarks>
    ///     A declaration file's statements are all 'declare' - nothing about it ever reaches Luau - so
    ///     writing one to disk would only leave an empty, unmapped .luau sitting in the output tree.
    /// </remarks>
    [Fact]
    public void Compiles_Project_WithDeclarationFile_WritesNoOutputForIt() =>
        Utility.WithTempProject(
            [("globals.d.loom", "declare let global_number: number;"), ("main.loom", "let x = global_number;")],
            (compilationUnit, result) =>
            {
                Utility.AssertNoErrors(result);
                Assert.False(File.Exists(Path.Combine(compilationUnit.Config.Files.OutputDirectory, "globals.d.luau")));
                Assert.True(File.Exists(Path.Combine(compilationUnit.Config.Files.OutputDirectory, "main.luau")));
            }
        );

    /// <remarks>
    ///     Which of the two files is reported depends on the order they were analyzed in, so this pins the
    ///     pair rather than either side of it: the diagnostic sits in one of them and names the other.
    /// </remarks>
    [Fact]
    public void Reports_AnAmbientName_DeclaredTwiceInOneRoot() =>
        Utility.WithTempProject(
            [("first.d.loom", "declare let version: number;"), ("second.d.loom", "declare let version: number;"), ("main.loom", "print(version);")],
            (_, result) =>
            {
                var diagnostic = Assert.Single(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.DuplicateGlobal);
                var named = diagnostic.Span.File.Name == "first.d.loom" ? "second.d.loom" : "first.d.loom";

                Assert.Equal($"'version' is already declared by '{named}'.", diagnostic.Message);
                Assert.Equal(
                    "a project's declaration files share one ambient scope, so each name may only be declared once across them",
                    diagnostic.Hint
                );
            }
        );

    /// <remarks>Types and values are looked up separately, so one name in each namespace is two names.</remarks>
    [Fact]
    public void Allows_AnAmbientType_AndAnAmbientValue_OfOneName() =>
        Utility.WithTempProject(
            [
                ("values.d.loom", "declare let version: number;"),
                ("types.d.loom", "type version = string;"),
                ("main.loom", "let name: version = \"1.0\";\nprint(name, version);")
            ],
            (_, result) => Utility.AssertNoErrors(result)
        );

    /// <summary>
    ///     A global's declared type can itself reference another ambient declaration from the same
    ///     <c>.d.loom</c> file - the <c>DateTimeStatic</c>-shaped pattern every hand-written package
    ///     typing leans on. Resolving that reference from a <em>different</em> file used to re-visit the
    ///     declaring file's node with the consuming file's own semantic model, which had no bindings for
    ///     it and so reported the referenced type as missing. Issue found writing jecs.d.loom.
    /// </summary>
    [Fact]
    public void ResolvesAGlobalsType_ThroughAnotherGlobal_FromADifferentFile() =>
        Utility.WithTempProject(
            [
                ("ambient.d.loom", "declare sealed interface Foo { bar: number; }\ndeclare let foo: Foo;"),
                ("main.loom", "print(foo.bar);")
            ],
            (_, result) => Utility.AssertNoErrors(result)
        );

    /// <summary>
    ///     Models the <c>jecs.d.loom</c> + <c>jecs.luau</c> pairing a native binding publishes: typings for a
    ///     Luau module the compiler does not generate. An exported declare crosses the module boundary like
    ///     any other export, resolving to a property read on whatever the hand-written sibling's own
    ///     <c>require</c> returns - and that sibling, not anything compiled from the declaration file itself,
    ///     is what ends up in the output tree under the declaration file's own would-be output name.
    /// </summary>
    [Fact]
    public void Compiles_Project_WithExportedDeclare_RoutingThroughItsRuntimeSibling() =>
        Utility.WithTempProject(
            [
                ("jecs.d.loom", "export declare let world: number;\ndeclare let internalOnly: number;"),
                ("jecs.luau", "local jecs = {}\njecs.world = 42\nreturn jecs\n"),
                ("main.loom", "import { world } from \"./jecs\";\nlet x = world;")
            ],
            (compilationUnit, result) =>
            {
                Utility.AssertNoErrors(result);

                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
                Assert.Contains("require(\"./jecs\")", main.RenderedLuau);
                Assert.Contains("jecs.world", main.RenderedLuau);

                var outputSibling = Path.Combine(compilationUnit.Config.Files.OutputDirectory, "jecs.luau");
                Assert.True(File.Exists(outputSibling));
                Assert.Equal("local jecs = {}\njecs.world = 42\nreturn jecs\n", File.ReadAllText(outputSibling));
                Assert.False(File.Exists(Path.Combine(compilationUnit.Config.Files.OutputDirectory, "jecs.d.luau")));
            }
        );

    [Fact]
    public void Compiles_EveryFile_WhenAnotherFileHasDiagnostics() =>
        Utility.WithTempProject(
            [("bad.loom", "import { } from \"./math\""), ("good.loom", "let x = 1;")],
            (_, result) =>
            {
                Assert.Equal(2, result.Files.Count);
                Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.EmptyImportClause, "Import declaration must name at least one member.");

                var good = Assert.Single(result.Files, file => file.SourceFile.Name == "good.loom");
                Assert.Contains("const x = 1", good.RenderedLuau);
            }
        );

    [Fact]
    public void Compiles_ASingleFile_ReportingItsImportsAsUnresolvable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");

            var path = Path.Combine(directory, "src", "main.loom");
            File.WriteAllText(path, "import { pi } from \"./math\"\nprint(pi);");

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            // the file is the whole unit, so nothing can satisfy the import — saying so beats binding it to
            // nothing, which is what a compile without a module graph used to do
            var compiled = new CompilationUnit(config).Compile(FileManager.LoadSingle(path));
            Assert.NotNull(compiled);
            Utility.AssertDiagnostic(compiled.Diagnostics, InternalCodes.ModuleNotFound, "Could not find module './math'.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <remarks>
    ///     Dropping the output directory after the unit has loaded its files makes every output path throw,
    ///     which stands in for any stage failing: the unit has to report it rather than let the exception out.
    /// </remarks>
    [Fact]
    public void Reports_FilesTheCompilerGaveUpOn_InsteadOfThrowing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            File.WriteAllText(Path.Combine(directory, "src", "main.loom"), "let x = 1;");

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            var compilationUnit = new CompilationUnit(config);
            config.Files.OutputDirectory = null!;

            var result = compilationUnit.Compile();
            Assert.Empty(result.Files);

            var failure = Assert.Single(result.Failures);
            Assert.Equal("main.loom", failure.File.Name);

            var compilerError = failure.Diagnostics.Find(diagnostic => diagnostic.Code == InternalCodes.CompilerError);
            Assert.NotNull(compilerError);
            Assert.Contains(compilerError, result.Diagnostics.Set);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <remarks>
    ///     Emptying the source directory after the unit has loaded its files makes the module resolver throw
    ///     while the graph is built, which is a failure of the unit rather than of any one file.
    /// </remarks>
    [Fact]
    public void Reports_EveryFile_WhenTheModuleGraphCannotBeBuilt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            File.WriteAllText(Path.Combine(directory, "src", "main.loom"), "import { helper } from \"./util\"\nprint(helper);");
            File.WriteAllText(Path.Combine(directory, "src", "util.loom"), "export let helper = 1;");

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            var compilationUnit = new CompilationUnit(config);
            config.Files.SourceDirectory = "";

            var result = compilationUnit.Compile();
            Assert.Empty(result.Files);
            Assert.Equal(["main.loom", "util.loom"], result.Failures.Select(failure => failure.File.Name).Order());

            // one error for the unit, not one per file
            var compilerError = Assert.Single(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.CompilerError);
            Assert.Contains("module graph", compilerError.Message);
            Assert.All(result.Failures, failure => Assert.Contains(compilerError, failure.Diagnostics.Set));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Compiles_WithTheUnitsDiagnosticOptions_IncludingModuleDiagnostics()
    {
        var options = new DiagnosticOptions();
        Utility.WithTempProject(
            [("main.loom", "import { square } from \"./missing\"")],
            (unit, result) =>
            {
                Assert.Same(options, unit.DiagnosticOptions);
                Assert.Same(options, result.Diagnostics.Options);

                var file = Assert.Single(result.Files);
                Assert.Same(options, file.Diagnostics.Options);

                var moduleDiagnostics = unit.ModuleGraph?.GetDiagnostics(file.SourceFile);
                Assert.NotNull(moduleDiagnostics);
                Utility.AssertDiagnostic(moduleDiagnostics, InternalCodes.ModuleNotFound, "Could not find module './missing'.");
                Assert.Same(options, moduleDiagnostics.Options);
            },
            diagnosticOptions: options
        );
    }

    private static LoomConfig GetConfig()
    {
        var config = ConfigReader.LocateFromDirectory(AssemblyFixture.Snapshots);
        Assert.NotNull(config);
        Assert.Equal(AssemblyFixture.Snapshots, config.ProjectDirectory);

        return config;
    }


}
