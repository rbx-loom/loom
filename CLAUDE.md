# CLAUDE.md

Loom: domain-specific language for Roblox, transpiles to Luau. C# / .NET 10, xUnit tests. WIP — breaking changes allowed. Repo: https://github.com/rbx-loom/loom

## Commands

```bash
dotnet restore
dotnet build
dotnet test                     # full test suite (Loom.Testing, xUnit)
dotnet test --filter "FullyQualifiedName~ParserTest"   # single test class
dotnet run --project Loom.CLI -- build <dir>           # compile a Loom project (dir with loom-config.toml, default "."), TestProject exists for testing changes
dotnet run --project Loom.Tools -- ast <file.loom>     # dump AST for a file
dotnet run --project Loom.Tools -- compile <file.loom> # emit one file's Luau on stdout, diagnostics on stderr — how Snapshots/Luau fixtures are regenerated
dotnet run --project Loom.Tools -- generate-ast-snapshots Loom.Testing/Snapshots/AST  # regenerate AST snapshot files
```

### Agent tooling (`scripts/`)

Token-saving wrappers for the two most expensive things to do by hand in this repo — read [scripts/README.md](scripts/README.md) for full details before falling back to raw `Read`/`Grep`/`dotnet run`:

- `scripts/loom-intrinsic.sh <Name>` — prints one `declare` block from the generated Roblox intrinsic files (`Loom.Core/TypeChecking/Intrinsic/generated/*.loom`, ~29k lines total) by brace-matched extraction, instead of grepping/reading the whole file. `--list [filter]` lists declared names.
- `scripts/loom-regen-ast-snapshots.sh` — regenerates `Snapshots/AST/*.ast` and auto-reverts any file whose diff is line-ending-only (CRLF drift from files predating the LF-only generator), so `git status` only shows genuine content changes.
- `scripts/loom-new-luau-snapshot.sh <name>` — compiles `Snapshots/Luau/<name>.loom` and writes `<name>.luau` in the exact byte shape `CompilerTest.AssertCompiled` expects (no trailing newline); refuses to write if compilation reported an error.

CI (`.github/workflows/ci.yml`): `dotnet test -c Release` with coverage → Coveralls. Tests required for all PRs. Verify with `dotnet build` then `dotnet test`
before claiming done.

## Solution layout

- `Loom.Core/` — the compiler.
    - `Lexing/` — `Lexer`, rule-based (`LexerRules.cs`)
    - `Parsing/` — `Parser` (partial class split: `.Declarations`, `.Expressions`, `.Statements`, `.Types`), `AST/` one file per node type (~90 files)
    - `Resolving/` — `Resolver` (partial: `.ControlFlow`, `.Declarations`, `.Interfaces`, `.Modules`, `.Patterns`, `.SymbolTable`), `SemanticModel`, scopes +
      `Symbols/`. One class per `SymbolKind`, so `Symbol.Kind` is a fact about the class rather than a
      constructor argument, and a kind-specific member has somewhere to live. `Symbol` splits into
      `TypeSymbol` (looked up in type position) and `ValueSymbol` (value position) — which is the same
      split the resolver's two per-scope lookup tables make, and `Symbol.IsTypeKind` answers it for callers
      holding a bare kind (`SymbolHierarchyTest` keeps the two from drifting). Attributes hang off `Symbol`
      itself: reading one should not require first knowing what kind of symbol you have
    - `FlowAnalysis/` — `FlowAnalyzer`, flow state for control-flow reasoning
    - `TypeChecking/` — `TypeChecker` (partial: `.Enums`, `.Generics`, `.Interfaces`, `.ControlFlow`, `.Declarations`, `.Invocations`, `.Match`, `.MemberAcess`,
      `.Operators`, `.TypeNodes`, , `.Check` (bidirectional/contextual typing)), plus `TypeInferrer`, `TypeNarrower`, `TypeSimplifier`, `TypeSolver`; Intrinsics
      for injecting .loom files into all Loom programs
      `Types/` one file per type kind (union, intersection, literal, generic, etc.); `Intrinsic/` + operator binders/rules.
      `KeyOfType`, `IndexedType`, `MappedType` (`[K from keyof(T)]: T[K]`) and `ConditionalType` (`T is U ? A : B`, and the
      n-armed `match T { ... }` it is the two-armed case of) are all *deferred operators*: they answer nothing at the
      declaration, where the target is still a parameter, and are resolved by `TypeSubstitution.Apply` once an
      instantiation supplies one. Adding another means a case there, in `TypeSolver.Transform`, and in
      `TypeSimplifier.Normalize`. `TypeMatcher` measures a subject against a pattern and binds its `let` names;
      `ConditionalTypeEvaluator` picks the arm, distributes over a union for `match each`, and unrolls a tail-recursive
      arm iteratively under two bounds (small for nesting, large for tail steps)
    - `Generation/` — `LuauGenerator` (partial: `.Declarations`, `.Events`, `.Expressions`, `.Interfaces`, `.Match`, `.Statements`, `.Types`),
      `LuauOperatorMap.cs`, `Macros/` with `IMacroProvider` implementations under `Macros/Providers/` (Array, Range, Number, Result, Instance, global
      invocations)
    - `Modules/` — import/export graphing, resolution, and Luau require() path resolution. A relative specifier
      resolves inside the importing file's own root; a bare one (`math`, `scope/math`, `math/vector`) names the root
      publishing that package — its `init.loom` when no subpath follows — and only a package the importing root
      declares in `[dependencies]` is importable. A folder folds into whichever index file it has (`ModuleResolver
      .IndexFileName`/`.SecondaryIndexFileName`) — `init.loom`, mirroring Rojo's own folding, or `main.loom` for a
      package authored without Rojo's convention in mind; `init` wins where a folder has both. Luau's own
      require-by-string resolver only knows how to fold `init` on its own, so a fallback require (no Rojo project to
      say where the output landed) that reached a module through a `main` fold has to name the file outright —
      `ModuleResolver.LiteralRequirePath`/`.FoldedThroughSecondaryIndex`, used only by `ModuleRequirePathResolver`'s
      fallback branch, everything else still resolves by specifier text alone
    - `Diagnostics/` — `DiagnosticBag`, severities, `InternalCodes.cs`. Errors flow through diagnostics, never exceptions (top-level `Compiler.Compile` catch =
      compiler bug path).
    - `Pipeline/` — `Compiler.cs` pipeline orchestration; `CompilationUnit.cs` multi-file compile with a two-phase parse-analyze step to support modules.
      A unit spans a `SourceRootSet`: one `SourceRoot` (a `LoomConfig` plus the files under its source directory) per project it compiles — the entry
      project, plus one per source-distributed dependency. The boundary a relative import may not cross comes from `Roots.Of(file)`, never from the unit's
      own `Config`; where a file's Luau goes is `Roots.OutputPathOf(file)`'s single answer, used by both the writer and the require-path resolver.
      **Install-location contract:** a dependency's output is written into the *entry* project's output directory, under
      `<output>/packages/<scope>/<name>` — compiled output is consumer-specific (it names the entry project's runtime and is checked against its project
      type's intrinsics), so it cannot live beside sources a package manager may share. One `$path` covering the project's output therefore covers every
      package, whatever the PM did with the sources. `no_emit` is read off the entry project alone for the same reason. `ProjectLoader` is what a host
      (CLI, watch, LSP) asks for those roots: it reads `loom-lock.toml`, and `DependencyResolver`'s lock-taking overload resolves it — directories from
      `PackageLayout`, then two checks the map overload cannot make, that every manifest in the build asks only for versions the lock accepts and that the
      package installed in a directory *is* the version locked. Both are a stale lock, which only a package manager can fix, so neither is compiled
      through. A project with no `[dependencies]` needs no lock; one that declares them and has no lock has never been resolved, and guessing from the
      requirements is exactly what the lock exists to prevent. **A package's `dev = true` dependencies are not the consumer's:** only the entry project's
      are resolved, installed and required to be locked — a package's development dependencies are what its own tests are written against, and a package
      whose shipped source imports one has a mislabelled dependency, which the unresolved import says. Same line in `LockResolver`, `PublishedPackage`
      and `LockFile.Satisfies(config, includeDevelopmentOnly)`
- `Loom.Luau/` — Luau output AST + renderer (`LuauFactory`, `RenderState`, `AST/`)
- `Loom.Config/` — `loom-config.toml` reader (Tomlyn). `ProjectType` (default `game`), `Debug` (default `false`, for emitting debug diagnostics) `FilesConfig`:
  `SourceDirectory` (default `src`) → `OutputDirectory` (default `dist`). Package identity lives here too: `[package]` (`PackageConfig`, with `PackageName` and
  semver `Version` value types, plus `Realm`), `[dependencies]` (`Dependency`), `[registry]` (`RegistryConfig`). A dependency's requirement is
  parsed into a `VersionRequirement` while reading, not left as text: every clause form (`^1.2`, `~1.2.3`, `>=1.4, <2`, `*`) names an interval and
  comma-separated clauses intersect, so a requirement *is* one interval — which is what makes `Satisfies` a predicate over an index's published
  versions and `Intersect` closed, the two questions resolution is made of. Emptiness is never represented: an unsatisfiable requirement is rejected at
  parse, and disagreeing requirements come back from `Intersect` as `null`. A pre-release satisfies a requirement only when one of its bounds names a
  pre-release of the same `major.minor.patch`, so `>=1.2.0` does not quietly pick up `1.3.0-beta.1`. `[realms]` maps a directory under the source
  directory to `shared`/`client`/`server`; `SourceRoot.RealmOf` answers with the *longest* directory naming a file, so a realm declared inside another narrows
  it rather than being shadowed, and a project declaring none has one realm and no boundary to cross. `ManifestEditor` is the only thing that *writes* a
  manifest, and writes it as a text edit — a table header and a key line, never a re-serialization — since the comments, key order and line endings in a
  `loom-config.toml` are its author's; it answers `null` rather than touching more than the one line an entry is written on. `ConfigReader` never throws on
  a manifest problem — malformed manifests come back as `null` plus `ConfigDiagnostic`s out of `LocateFromDirectory`. `[files]` directories are validated there too
  (non-empty, relative, path-legal), since nothing downstream can report one that isn't: they are resolved as real paths and a stage throwing is the
  compiler-bug path. `loom-lock.toml` is the other half of that contract: the manifest says which versions are *acceptable*, the lock (`LockFile`,
  `LockedPackage`, read by `LockFileReader` the same never-throwing way) says which ones were *chosen* — one `[[package]]` per package, plus the
  `dependencies` naming the rest, so a lock is the resolved graph and `LockFileReader` can reject one that is not closed. It carries no paths: a lock is
  committed and read again on another machine, so where a package landed stays the package manager's answer (`DependencyResolver`'s `packageDirectories`)
  while *which version* is the lock's. `ToToml` is deterministic (ordered entries, fixed key order, `\n`) because two machines have to write the same
  bytes, and `Satisfies(LoomConfig)` is how a package manager asks whether the lock still covers the manifest instead of re-resolving. `PackageLayout` is
  the other half of that: `<project>/packages/<scope>/<name>` is where a package manager installs sources and where the compiler reads them — not a
  setting, for the reason `FilesConfig.PackagesDirectoryName` isn't one, and deliberately not keyed by version, since one build compiling two copies of a
  package is not a shape anything downstream supports. `[registry] index` and a lock's `source` are read by one rule (`IndexLocation`), since the second
  is written from the first: an index is as legitimately a directory — vendored, or a test's fixtures — as it is a URL, so neither asks for a URL
- `Loom.Packages/` — the package manager: the tool side of the line the compiler draws. `IPackageIndex` is all resolution needs from the outside world
  (what is published, and how to install it), so `LocalPackageIndex` — a directory of `<index>/<scope>/<name>/<version>`, each version a Loom project of
  its own — is a whole offline registry and the fixture every test resolves against; a network index implements the same interface later. Every member
  reports through `ConfigDiagnostic`s rather than by throwing, `Publications` included: an empty result is the one answer meaning *no such package*, so an
  index that could not say has to say so — a failure read as an empty result sends somebody whose network is down looking for a package that exists.
  `PublishedPackage.Yanked` is the other thing only a registry can state, and its whole point is the asymmetry: `LockResolver.Choose` and `PackageAdder`
  pass over a yanked version because both are choosing anew, while a version a lock already pins is kept and `PackageInstaller` installs it without asking
  — a yank withdraws a version from being taken up, and a build already on it is precisely what it is not trying to break.
  `LockResolver` turns requirements plus an index into a `LockFile`: every requirement on a package intersects into the one interval a
  `VersionRequirement` already is, so combining dependents needs no search and the newest published version inside that interval is the answer. It
  deliberately does *not* backtrack — if the newest version one package allows leaves another unsatisfiable, that is reported as a conflict naming both
  sides, not searched around; requirements are re-derived from the currently chosen versions each round (a requirement written by a version no longer
  chosen is not a requirement) under a round bound, so a graph that will not settle is reported rather than spun on. `PackageInstaller` copies into
  `PackageLayout`'s directories, comparing the *installed* version against the lock rather than any timestamp — a directory holding the right version is
  right however it got there, which is what makes vendoring by hand and installing from an index the same thing downstream. `PackageManager.Restore` is
  the one call a build makes: resolve only when the lock does not cover the manifest (keeping every version the old lock still allows, so one changed
  requirement does not bump everything else), install only what is missing or wrong, and open no index at all when both already hold — which is what
  lets a project with its packages present build with no registry reachable. `PackageAdder` is the other direction — the half that changes what a project
  *asks for*: a `PackageRequest` naming no version is answered from the index (compatibility with the newest release published, since a request with no
  opinion is not a request for a pre-release), written into the manifest by `ManifestEditor`, and only then restored. The manifest is written first because
  resolution reads the manifest, so a request that turns out not to be resolvable puts the file back — a failed `add` leaves the project as it found it.
  `PackagePublisher` splits publishing the way the tool/compiler line is drawn elsewhere: `Prepare` answers what a version consists of (the manifest, the
  source directory, and the README/LICENSE/CHANGELOG a reader wants — never the output directory, the installed packages or the lock, each of which is one
  consumer's answer rather than the package's), and `IPackageIndex.Publish` answers where it goes, so a local directory taking a copy and a registry taking
  an upload differ in nothing else. `CanPublish` is asked before a caller does the work of satisfying itself the version is fit to publish, since a version
  already published is never replaced whatever is offered for it
- `Loom.CLI/` — entry point; locates config, runs `PackageManager.Restore`, asks `ProjectLoader` what the project compiles, compiles the unit, prints
  debug info. `Projects` is how every verb finds the project it was pointed at and reports what stopped it before a file was read. `PackageCommands` holds
  the two verbs that work on packages rather than code (`add`, `publish`) and is deliberately thin — deciding what to write and what to send is
  `Loom.Packages`' job, which is testable without a terminal. `publish` compiles with `NoEmit` before it publishes: everything else a publish gets wrong can
  be fixed by publishing the next version, but source that does not compile is in the index for good — `--allow-dirty` skips that check for a publisher who
  knows something this machine does not, and says so on the way past. `add` is the one verb whose positionals are not the
  project directory (they are the packages to add), so it takes `--project` instead.
  `Include/loom_runtime.luau` = runtime support emitted alongside output. A watch restarts on `loom-config.toml`, the Rojo project *or* `loom-lock.toml` — a
  package manager installing a dependency changes which projects the unit spans and a unit already built cannot grow a root; renames count too, since
  installing atomically is a write to a temporary file followed by one
- `Loom.LanguageServer/` — LSP server (OmniSharp). One handler per request, all registered in `Program.cs`, all answering off the `DocumentStore`:
  it keeps one `CompilationUnit` per project root and recompiles the open file on every change. The pieces the handlers share:
    - `CompletionSnapshot` — rebuilt from each compile; answers "what may be written at this offset" (member scope, import list, attribute list,
      module specifier, type vs value position — plus keywords and built-in type names, which are never symbols, and `Importable`, the names other
      modules export that this file has not imported). A completion item's detail and documentation are `Func<>`s resolved on the client's resolve
      request, never eagerly: a project has thousands of names in scope on every keystroke and the client reads at most one
    - `DeclarationDisplay` renders a symbol the way its declaration reads; `SymbolMarkdown` composes the hover body; `DocumentationBlock` parses
      `@param`/`@returns` out of a doc comment; `CallSiteFinder` locates the call the cursor is inside and who it calls
    - `SymbolReferences` walks every analyzed tree to invert the resolver — what refers to *this* symbol — behind references, rename, prepare-rename
      and document highlight. An import binds the exporting module's own `Symbol` instance, so one identity spans every file; a reference whose token
      text differs from the symbol's name is an alias and is left alone by rename
    - `ImportCatalog`/`ImportEdits` decide what a file could import and build the edit that imports it, shared by auto-import completion and the
      "Import 'X'" quick fix
    - `DocumentStore` splits recording an edit from compiling it: `Change` only records, `Compile`/`TryGetState` bring the document up to date on
      demand, and only `didChange`'s diagnostic publish is deferred (via `Debouncer`) because it is the one thing nobody is blocked on. Every dirty
      buffer of a unit goes into one `Recompile` together, and `Close` reverts the file to its saved text — the unit keeps whatever text it was last
      handed, and an editor discards unsaved edits when a document closes
    - `DocumentStore` builds its units through `ProjectLoader` too, so an editor sees the packages a build sees; a project whose dependencies cannot be
      loaded (no lock yet, one not installed) still gets a unit over its own files, since answering nothing about the file on screen is worse than
      answering it without its packages
    - `DiagnosticPublisher` reports every file the compile found something in, not just the edited one, and remembers what it said so a file whose
      errors are gone gets an empty set. It only ever clears files the compile covered: a workspace may hold more than one project
    - `Conversion` owns `DiagnosticsFor`/`DiagnosticsByFile`: a null result means the file was not analyzed, and an empty set is how a client is told
      to drop what it is still showing
    - `WatchedFilesHandler` takes the client's file-watcher notifications (the client watches, not the server — it already knows the user's exclusions
      and would not hear its own writes back) and feeds them to `DocumentStore.ReloadFromDisk`, debounced so a branch switch compiles once rather than
      once per file. A file open in the editor is skipped: its buffer is what every request is answered against
    - `FilePaths.Same`, which is `Loom.Core.Text.PathComparison` — never compare paths with `==`. A client's `file:` URI round-trips a Windows drive
      letter in lower case while the compiler's path came from the project directory, so an ordinal comparison silently matches nothing. *Module
      specifiers* are deliberately case-sensitive, though (Roblox requires are), so `SourceRootSet.CanonicalPath` is what keeps a path entering the
      unit from a new source spelled the way the roots already spell it
- `Loom.TypeGenerator/` — Loom code generator to define types for the Roblox API; tests depend on these types to be generated to pass
- `Loom.Tools/` — dev tooling (AST dump, snapshot generation)
- `Loom.Testing/` — all tests, one test class per compiler stage/component
- `TestProject/` — sample Loom project (src/dist + loom-config.toml) for end-to-end runs

## Pipeline

`Lexer → Parser → Resolver → FlowAnalyzer → TypeChecker → LuauGenerator → LuauTree.Render()` (see [Compiler.cs](Loom.Core/Pipeline/Compiler.cs)). Every stage returns a
result carrying a `DiagnosticBag`; stages after the parser walk the AST via the visitor pattern. New syntax means touching parser AND resolver AND type checker
AND generator — not just parse + emit (see CONTRIBUTING.md).

## Tests

- Framework: xUnit + coverlet. Shared helpers in [Utility.cs](Loom.Testing/Utility.cs), split into one partial file per pipeline stage it wraps
  (`Utility.Lexing.cs`, `.Parsing.cs`, `.Resolving.cs`, `.FlowAnalysis.cs`, `.TypeChecking.cs`, `.Generation.cs`) plus the cross-cutting `.Assertions.cs`,
  `.Projects.cs` (temp-project/`CompilationUnit` scaffolding), and `.LanguageServer.cs` (document-store scaffolding) — add a new helper to whichever file
  already matches its concern.
- Layout mirrors `Loom.Core`'s own folders — `Lexing/`, `Parsing/`, `Resolving/`, `FlowAnalysis/`, `TypeChecking/`, `Generation/`, `Modules/`,
  `Diagnostics/`, `Pipeline/` — plus `Config/`, `Packages/`, `LanguageServer/` for the pieces outside the compiler proper, and `Runtime/` (below). A test
  class's folder follows what it actually exercises (its `using`s), not what its name suggests. `Assembly.cs` and `Utility*.cs` stay at the root since
  every folder's tests depend on them.
- Snapshot tests: `Loom.Testing/Snapshots/AST/*.loom` + `.ast` pairs (parser), `Snapshots/Luau/*.loom` + `.luau` pairs (full-pipeline codegen). Adding a
  language feature usually adds a snapshot pair. Regenerate AST snapshots with Loom.Tools.
- Per-stage expectations (from CONTRIBUTING.md): parser — valid parses/invalid errors/AST shape; resolver — symbols declared, scope rules; type checker —
  inference, assignability, and for new types test `Equals`, `IsAssignableTo`, `ToString`; codegen — Luau AST correct, rendering valid, edge cases (escaping,
  empty collections).
- Round-trip tests: `Loom.Testing/Runtime/*.luau` assertion bodies pair by name with a `Snapshots/Luau` case and actually *execute* the emitted serializers
  on an embedded Luau — the C# test class asserting on each pair lives alongside the `.luau` files in that same folder (e.g.
  [Runtime/SerializationRuntimeTest.cs](Loom.Testing/Runtime/SerializationRuntimeTest.cs)). Snapshots only prove the output did not change; these prove it
  works, and caught several bugs snapshots could not (wrong value shape, a shadowed local, an undersized buffer). The interpreter comes from the `NuLua.Luau`
  package, so they need nothing installed and run under a plain `dotnet test`.
- A test class that outgrows one file splits into partials the same way a `Loom.Core` stage does (see Conventions below) — `TypeCheckerTest`,
  `ResolverTest`, `ParserTest`, `SerializationSchemaTest`, `CompilationUnitTest`, and `SourceRootTest` are already split this way, one file per region of
  cases (`TypeCheckerTest.Match.cs`, `ResolverTest.Destructuring.cs`, ...). A class small enough to read in one sitting stays as one file - splitting is
  for navigability once it isn't, not a rule to apply preemptively.
- Conformance tests: `Loom.Testing/Conformance/semver.json` and `package-name.json` are checked in here *and* in `rbx-loom/loom-pm`, and both test suites
  execute them — Loom's requirements are not ordinary semver (one interval and no `||`, unsatisfiable is a parse error, a pre-release needs a bound naming
  one of the same release), so no off-the-shelf library agrees with either side and only each other keeps the registry and the compiler honest. C# is the
  reference implementation: when the two disagree C# is right, unless the disagreement is a bug here — in which case fix it here and add the case. Grow the
  files from bugs; a change to either copy belongs in both repositories.

## Conventions

- PascalCase classes/methods/public properties; camelCase locals; private fields `_underscore` prefixed (except private consts); no abbreviations in names.
- Nullable + ImplicitUsings enabled everywhere; primary constructors used (e.g. `Compiler(CompilationUnit unit, SourceFile file)`).
- Big classes split as partial files by concern (`Parser.Expressions.cs`, `TypeChecker.Generics.cs`) — follow that pattern when a stage grows.
- Catch bloat while it's happening, not in a later cleanup pass: if a change you're making grows a file past a single concern, or plants the same logic at two or more call sites, split the partial / extract the shared helper / fix the naming inconsistency as part of that same change. Recognizing this after the fact (a separate refactor PR, a review catching it later) is a worse outcome than noticing it while the context is already loaded — this compiler should read as navigably as TypeScript's or Roslyn's, and that's upkeep done continuously, not batched.
- One AST node / one type kind per file.
- Commit style: conventional-commit prefixes `feat:`/`fix:`/`test:`/`docs:`/`ref:`/`perf:`/`chore:`/`style:` (see git log). Commit as you go, title only — no description, no co-author, just `type: name`. The word before the colon is the conventional-commit *type* (what kind of change this is), not the project area or module it touches — `fix: cross-file ambient global type resolution`, not `typechecking: cross-file ambient global type resolution`. Commits already in history using an area as the prefix are fine as they are; this is guidance for new ones, not a cleanup task.
- No inline comments; doc comments (`###`) only when necessary.
- Source files: Loom source uses `.loom` extension; output `.luau`. Indices are 1-based (Luau semantics). Immutability by default (`let` → `const`/local, `mut`
  for mutable).
- `mut` is a **capability, not a guarantee**. Giving one up is safe, gaining one is not: a mutable member (property, indexer, array element) satisfies an
  immutable one, and never the reverse. An immutable target can only be read through, so its type is covariant; a mutable one is invariant, since anything
  written through it is read back through the source. One rule, in `ObjectType.IsMemberAssignable` and mirrored by `ArrayType.IsAssignableTo` and
  `TypeSolver.UnifyObjectTypes` — assignability and unification answer the same question and must not diverge. Loom cannot promise an immutably-typed value
  never changes (it does not track who else holds a mutable alias), so reading `mut` as a guarantee would cost the widening and buy nothing.
- Loom comments: `##` line, `#: … :#` block, `###` doc. A run of `###` lines documents the declaration below it and is the only comment form anything
  reads — the lexer pairs each run with the token it precedes in `SourceFile.Documentation`, and `Node.Documentation` looks it up. `@param name text`
  and `@returns text` inside one are pulled out for signature help.
- ReSharper/Rider settings in `Loom.sln.DotSettings`; formatting handled by linter, don't hand-fight it.

## Gotchas

- Testing imports both plus `Type = Loom.TypeChecking.Types.Type` alias to dodge `System.Type` clash. `Loom.Packages` names a package version in nearly
  every file, so it aliases `Version = Loom.Config.Version` project-wide in its csproj instead of per file.
- `DiagnosticOptions.FailFast` (per `CompilationUnit`, threaded into every stage's `DiagnosticBag`) prints the first error and exits the process. Off by
  default; only `Loom.CLI` opts in. Options are handed out per file by `CompilationUnit.DiagnosticOptionsFor` — a dependency's files never fail fast, so the
  error the build stops on is the one naming the package.
- A dependency's diagnostics are not the consumer's to fix: `Compiler` runs every dependency file's bag through `DiagnosticBag.AttributedTo`, which drops
  warnings and info and collapses errors into one `PackageFailedToCompile` per file carrying the first underlying error. `DiagnosticOptions
  .ReportDependencyDiagnostics` (CLI: `--dependency-diagnostics`) turns that off for debugging a package from a project consuming it. Opening a package's own
  files in the LSP needs no flag — the package is the entry root of its own unit there.
- Every compiled file ends with a `return` now, even one with nothing to export — `Compiler.EnsureChunkReturnsAValue`
  appends `return nil` when `LuauGenerator` didn't already end the tree with its own `return` of the file's exports.
  A ModuleScript with no runtime value to export (type/enum-only, or simply empty) used to compile with no `return`
  at all, which is only harmless for a Script/LocalScript — Roblox's `require()` raises "Module code did not return
  exactly one value" the moment anything actually imports one as a module. Deliberately *not* in `LuauGenerator`
  itself: hundreds of `Generation` tests call `LuauGenerator.Generate()` directly on an isolated snippet and assert
  on its exact statement list, so the padding return is appended one layer up, in the one place that's actually
  building output meant to be `require()`d. A round-trip `Runtime/*.luau` test that splices more code after the
  emitted body (`SerializationRuntimeTest`, `FutureRuntimeTest`, `SpreadRuntimeTest`) has to filter a bare
  `return nil` line back out first — Luau requires `return` to be the last statement of its block, same reason
  `FutureRuntimeTest` already dropped `loom_runtime.luau`'s own `return Loom` before splicing.
- A chained intrinsic access used to only rename its *last* step: `frame.absolute_position.y` compiled to
  `frame.absolute_position.Y` instead of `frame.AbsolutePosition.Y` — `GenerateRenamedAccess` read one `luau_name`
  attribute off the whole access expression (which `SemanticModel.GetPropertySymbol` resolves to the *final* step's
  symbol) and only ever renamed `names[^1]`, leaving every earlier step exactly as spelled in the Loom source. Harmless
  whenever an earlier step's Loom name happened to already match its Luau one; wrong the moment it didn't, which two
  or more intrinsic hops in one chain makes easy to hit (found building a Roblox GUI's fader-drag math). Fixed via
  `SemanticModel.GetPropertySymbolChain`/`InterfaceSymbol.GetPropertyChainAtPath`, which walk the same path but
  return one symbol *per step* instead of only the terminal one, so `GenerateRenamedAccess` can check every step's
  own attribute. Regression test: `Generates_ChainedMemberAccess_RenamesEveryStepWithALuauNameAttribute`.
- `x is T` is read as a type predicate or as a conditional type depending on whether a `?` follows the target, which the parser only finds out after reading
  it — so `_suppressOptionalSuffix` turns off the postfix `?` for the length of that target and is cleared only by `Parser.Bracketed`, for positions a closing
  bracket ends before the `?` could be reached. A target that really is optional therefore needs parens: `T is (number?) ? A : B`. A pattern's `let` binders
  are declared into the *arm's* scope by `DeclarePatternBinders`, not where they are written — `VisitFunctionType` opens a scope of its own, and
  `fn(): let R ? R : never` would otherwise lose `R` before the branch that uses it.
- A conditional alias emits no Luau body (Luau can express the answer but not the question): every use emits what it resolved to via `LuauTypeRenderer`, and a
  use still generic at emission falls back to `unknown` with a warning — never `any`, which would silence every check downstream instead of making the consumer
  narrow. A mapped type does have a Luau lowering (`{ [keyof<T>]: index<T, keyof<T>> }`), so it keeps its name at use sites.
- The resolver keeps ambient names (intrinsics + `.d.loom` globals) in a scope below the file's own, so a module declaration shadows them instead of
  colliding. Scope depth is therefore not a test for "top level of a module" — use `AtModuleScope()`. Imports resolve ahead of the file's statements, so a name
  may be used above the import that brings it in.
- `.d.loom` globals are scoped to the root that declared them (`GlobalSymbols`, keyed by name *and* namespace): a package cannot put ambient names in a
  consumer's scope — its public surface is its exports — and one name declared by two of a root's declaration files is an error. Intrinsics are not
  root-scoped; they reach every file of every root.
- Output path derived via `Path.GetRelativePath` from the source directory, then re-rooted under the output directory
  ([FileManager.cs:19](Loom.Core/Pipeline/FileManager.cs)).
- `Loom.TypeGenerator` generates intrinsic types from the Roblox API that the test suite relies on to pass. The intrinsics are stored in
  `Loom.Core/TypeChecking/Intrinsics`.
- A Tomlyn `TomlConverter` may only read a *scalar* value. One that consumes a table (inline or not) desynchronizes the reader and silently swallows the
  table that follows it — which is why `[dependencies]` binds as `Dictionary<string, object>` and is read in `ConfigReader` instead of by a converter. Its
  typed deserializer also *ignores* a key it has no property for, so a table whose unknown keys have to be reported — `[dependencies]`, a lock file's
  `[[package]]` — is bound raw and read by hand for that reason too.
- PRs target `master`; open an issue before writing code (CONTRIBUTING.md).
