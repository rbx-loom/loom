using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Generation;
using Loom.Core.Lexing;
using Loom.Core.Parsing;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.Luau.AST;

namespace Loom.Core.Pipeline;

public sealed class Compiler(CompilationUnit unit, SourceFile file)
{
    private readonly List<DiagnosticBag> _pipelineDiagnostics = [];

    /// <summary>How much of <see cref="_pipelineDiagnostics" /> the parse phase contributed, which is what an analysis keeps and everything after it replaces.</summary>
    private int _parsePhaseDiagnostics;

    public SourceFile SourceFile { get; } = file;

    /// <summary>
    ///     Everything reported against the file so far. Unlike <see cref="CompiledFile.Diagnostics" /> this is
    ///     readable after a phase failed, which is the only place the compiler error of that failure lives.
    /// </summary>
    public DiagnosticBag Diagnostics => Reported(DiagnosticBag.Concat(_pipelineDiagnostics, Options));

    /// <summary>Reporting behavior of this file's stages, which is the unit's except for a dependency's files.</summary>
    private DiagnosticOptions Options => field ??= unit.DiagnosticOptionsFor(SourceFile);

    /// <summary>Null when a phase failed; <see cref="Diagnostics" /> says why.</summary>
    public CompiledFile? Compile()
    {
        var parsedFile = Parse();
        return parsedFile == null ? null : Analyze(parsedFile);
    }

    /// <summary>
    ///     Phase one: lex and parse. Runs for every file in the unit before any file is analyzed, so
    ///     that module dependencies can be resolved from the parsed trees and analyzed in order.
    /// </summary>
    public ParsedFile? Parse() =>
        RunPhase(() =>
            {
                var lexer = new Lexer(SourceFile, Options);
                var lexerResult = TrackDiagnostics(lexer.Tokenize());
                var parser = new Parser(lexerResult);
                var parserResult = TrackDiagnostics(parser.Parse());
                _parsePhaseDiagnostics = _pipelineDiagnostics.Count;

                return new ParsedFile(SourceFile, lexerResult, parserResult);
            }
        );

    /// <summary>
    ///     Phase two: everything after the parser. Diagnostics from <see cref="Parse" /> are carried over, as
    ///     are <paramref name="moduleDiagnostics" /> from building the unit's module graph, so the returned
    ///     file reports every diagnostic raised against it regardless of which phase found it.
    /// </summary>
    /// <returns>Null when the phase failed; <see cref="Diagnostics" /> says why.</returns>
    /// <remarks>
    ///     A file is analyzed more than once whenever an import's exports change, and it keeps the compiler
    ///     that parsed it - so an analysis starts by dropping what the last one reported. Without that, fixing
    ///     the module a file imports from leaves the error that fix resolved reported against it forever.
    /// </remarks>
    public CompiledFile? Analyze(ParsedFile parsedFile, DiagnosticBag? moduleDiagnostics = null) =>
        RunPhase(() =>
            {
                _pipelineDiagnostics.RemoveRange(_parsePhaseDiagnostics, _pipelineDiagnostics.Count - _parsePhaseDiagnostics);
                if (moduleDiagnostics != null)
                    _pipelineDiagnostics.Add(moduleDiagnostics);

                var resolver = new Resolver(parsedFile.ParserResult, unit);
                var semanticModel = TrackDiagnostics(resolver.Resolve());
                var flowAnalyzer = new FlowAnalyzer(semanticModel);
                TrackDiagnostics(flowAnalyzer.Analyze());
                var owningRoot = unit.Roots.Of(SourceFile);
                var accessingRealm = owningRoot.Config.Realms.Count > 0 ? unit.Roots.RealmOf(SourceFile) : (Realm?)null;
                var typeChecker = new TypeChecker(semanticModel, flowAnalyzer, accessingRealm);
                var typeCheckerResult = TrackDiagnostics(typeChecker.Check());
                var generatorResult = GenerateLuau(semanticModel, typeCheckerResult.Diagnostics);
                var renderedLuau = generatorResult == null ? "" : generatorResult.LuauTree.Render();

                return new CompiledFile(SourceFile)
                {
                    Root = unit.Roots.Of(SourceFile),
                    Path = unit.Roots.OutputPathOf(SourceFile),
                    Diagnostics = Reported(DiagnosticBag.Concat(_pipelineDiagnostics, Options)),
                    RenderedLuau = renderedLuau,
                    LuauTree = generatorResult?.LuauTree ?? new LuauTree([]),
                    ReturnType = typeCheckerResult.ReturnType,
                    SemanticModel = semanticModel,
                    Tree = parsedFile.Tree,
                    Tokens = parsedFile.LexerResult.Tokens,
                    TokensWithTrivia = parsedFile.LexerResult.TokensWithTrivia
                };
            }
        );

    /// <summary>
    ///     Lowers the file to Luau, or nothing when the unit was told not to emit any.
    /// </summary>
    /// <remarks>
    ///     Generation is the one stage whose only product is the output file. A unit that will not write one
    ///     walks every statement to build a tree, renders it to a string, and drops both. Three callers ask
    ///     for exactly that: the intrinsic bootstrap, which compiles the whole generated Roblox API to learn
    ///     its types; the language server, which recompiles on every keystroke and never writes anything; and
    ///     a <c>no_emit</c> build, which is a type check by definition.
    /// </remarks>
    private LuauGeneratorResult? GenerateLuau(SemanticModel semanticModel, DiagnosticBag typeCheckerDiagnostics)
    {
        if (unit.Config.NoEmit)
            return null;

        var generator = new LuauGenerator(semanticModel, unit.RuntimeImport, unit.ModuleRequirePaths, typeCheckerDiagnostics);
        var result = TrackDiagnostics(generator.Generate());
        EnsureChunkReturnsAValue(result.LuauTree);
        return result;
    }

    /// <summary>
    ///     A Luau chunk that is ever <c>require()</c>d must return exactly one value - Roblox raises "Module
    ///     code did not return exactly one value" otherwise. <see cref="LuauGenerator" /> already ends a file
    ///     with a <c>return</c> of its exports when it has any; a file with nothing to export (type/enum-only,
    ///     or simply empty) would otherwise compile with no return at all. Whether Rojo turns this particular
    ///     file into a ModuleScript (`require`d) or a Script/LocalScript (run directly, return value ignored)
    ///     is a filename convention this compiler has no visibility into, so the safe answer covers both: a
    ///     trailing `return nil` costs nothing where it's unread and fixes the crash where it would be.
    ///     Applied only here rather than inside <see cref="LuauGenerator" /> itself, so the many tests exercising
    ///     <see cref="LuauGenerator.Generate" /> directly on an isolated snippet keep seeing exactly the
    ///     statements their source produced.
    /// </summary>
    private static void EnsureChunkReturnsAValue(LuauTree tree)
    {
        if (tree.Statements is [.., Return])
            return;

        tree.Statements.Add(new Return(new NilLiteral()));
    }

    /// <remarks>
    ///     The compiler error of a failed phase is tracked like any other stage's diagnostics rather than
    ///     reported into a bag that goes nowhere, so it survives in <see cref="Diagnostics" /> for the caller
    ///     to report. Nothing else can carry it: the phase has no result to attach it to.
    /// </remarks>
    private T? RunPhase<T>(Func<T> phase)
        where T : class
    {
        try
        {
            return phase();
        }
        catch (Exception e)
        {
            var diagnostics = new DiagnosticBag(options: Options);
            _pipelineDiagnostics.Add(diagnostics);
            diagnostics.CompilerError(SourceFile, $"The compiler threw an exception!\n{e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    /// <summary>
    ///     What the build reports for this file: its own diagnostics when the file is the entry project's, and
    ///     one error naming the package when it belongs to a dependency the user is only consuming.
    /// </summary>
    private DiagnosticBag Reported(DiagnosticBag diagnostics) =>
        unit.PackageAttributionOf(SourceFile) is { } package ? diagnostics.AttributedTo(package, unit.DiagnosticOptions) : diagnostics;

    private T TrackDiagnostics<T>(T result)
        where T : DiagnosedResult
    {
        _pipelineDiagnostics.Add(result.Diagnostics);
        return result;
    }
}