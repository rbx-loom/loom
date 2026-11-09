using System.Diagnostics.CodeAnalysis;
using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.Core.Modules;

/// <summary>
///     Maps a module specifier as written in source (<c>"./math"</c>, <c>"serio"</c>) onto a
///     <see cref="SourceFile" /> of the compilation unit. Specifiers are extension-less; a relative one is read
///     from the importing file's directory and a bare one from the source directory of the root publishing the
///     package it names. Either way <c>"./math"</c> matches <c>math.loom</c>, <c>math/init.loom</c> or
///     <c>math/main.loom</c> — a folder folds into whichever index file it has, the same way Rojo folds an
///     <c>init</c> file into its folder, or a <c>main</c> file into its package the way <c>index.js</c> or
///     <c>__init__.py</c> would. <c>init</c> is tried first where a folder somehow has both.
/// </summary>
/// <remarks>
///     Paths are compared case-sensitively even where the file system is not, because Roblox requires are.
///     A declaration file is importable exactly like any other module - what it exports (as opposed to the
///     ambient names it merely declares, which every file of its own root sees without an import) is looked
///     up the same way, its <c>.d.loom</c> candidate tried after the plain <c>.loom</c> one so the two may
///     coexist without ambiguity. What it resolves to at runtime is a different question <see
///     cref="SourceRootSet.OutputPathOf" /> and <see cref="FileManager.CopyDeclarationRuntime" /> answer.
///     A relative specifier may not leave the importing file's own root: reaching out of one project and into
///     another is what a package specifier is for, not what <c>"../"</c> is for.
/// </remarks>
public sealed class ModuleResolver
{
    private const string IndexFileName = "init";

    /// <summary>
    ///     A second folder-index name, tried after <see cref="IndexFileName" /> — the <c>index.js</c> /
    ///     <c>__init__.py</c> convention rather than Rojo's own, for a package authored without Rojo's folding
    ///     in mind. Kept second rather than replacing <c>init</c> so a Rojo project's own convention is never
    ///     shadowed by it.
    /// </summary>
    private const string SecondaryIndexFileName = "main";

    /// <summary>How many leading segments of a specifier may name the package: <c>name</c> or <c>scope/name</c>.</summary>
    private const int MaximumPackageSegments = 2;

    private readonly Dictionary<string, SourceFile> _modulesByPath;

    /// <summary>The same modules keyed without regard to case, to tell a typo from a casing mistake.</summary>
    private readonly Dictionary<string, SourceFile> _modulesByPathIgnoringCase;

    private readonly SourceRootSet _roots;

    public ModuleResolver(IEnumerable<SourceFile> files, SourceRootSet roots)
    {
        _modulesByPath = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        _modulesByPathIgnoringCase = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var path = Path.GetFullPath(file.AbsolutePath);
            _modulesByPath.TryAdd(path, file);
            _modulesByPathIgnoringCase.TryAdd(path, file);
        }

        _roots = roots;
    }

    public static bool IsRelativeSpecifier(string specifier) =>
        specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal);

    public ModuleResolution Resolve(SourceFile importingFile, string specifier) =>
        IsRelativeSpecifier(specifier) ? ResolveRelative(importingFile, specifier) : ResolvePackage(importingFile, specifier);

    /// <summary>The specifier that would have named <paramref name="module" />, written from <paramref name="importingFile" />.</summary>
    /// <remarks>
    ///     A module of another root is named by its package rather than by a path, since no relative path
    ///     reaches it: a relative specifier stops at the edge of the root it is written in.
    /// </remarks>
    public string SpecifierOf(SourceFile importingFile, SourceFile module)
    {
        var moduleRoot = _roots.Of(module);
        if (moduleRoot != _roots.Of(importingFile) && moduleRoot.Package?.Name is { } packageName)
        {
            var subpath = ModulePath(moduleRoot.SourceDirectory, module);
            return subpath.Length == 0 ? packageName.ToString() : $"{packageName}/{subpath}";
        }

        var importingDirectory = Path.GetDirectoryName(Path.GetFullPath(importingFile.AbsolutePath)) ?? "";
        return PrefixDot(ModulePath(importingDirectory, module));
    }

    /// <summary>
    ///     The require path naming <paramref name="module" />'s own file, with no folder-index folded away —
    ///     used only where a relative require has to resolve on its own, without a Rojo mapping to say where
    ///     the file actually landed. Luau's require-by-string resolver folds a directory into an <c>init</c>
    ///     file on its own; folding through <see cref="SecondaryIndexFileName" /> is a Loom-only convention it
    ///     does not know about, so a require reaching one has to name the file outright either way.
    /// </summary>
    public static string LiteralRequirePath(SourceFile importingFile, SourceFile module)
    {
        var importingDirectory = Path.GetDirectoryName(Path.GetFullPath(importingFile.AbsolutePath)) ?? "";
        return PrefixDot(ModulePath(importingDirectory, module, foldIndexFile: false));
    }

    /// <summary>
    ///     Whether resolving <paramref name="specifier" /> from <paramref name="importingFile" /> reached
    ///     <paramref name="module" /> by folding a folder into a <see cref="SecondaryIndexFileName" /> file -
    ///     the one fold Luau's own require-by-string resolver cannot do on its own, unlike an
    ///     <see cref="IndexFileName" /> fold or a direct file reference, both of which it resolves correctly
    ///     without help. Only this case needs <see cref="LiteralRequirePath" /> in a fallback require; every
    ///     other relative require already works unresolved, specifier as written.
    /// </summary>
    public static bool FoldedThroughSecondaryIndex(SourceFile importingFile, string specifier, SourceFile module)
    {
        if (!IsRelativeSpecifier(specifier))
            return false;

        var importingDirectory = Path.GetDirectoryName(Path.GetFullPath(importingFile.AbsolutePath));
        if (importingDirectory == null)
            return false;

        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(importingDirectory, specifier)));
        var moduleDirectory = Path.GetDirectoryName(Path.GetFullPath(module.AbsolutePath));
        if (!string.Equals(basePath, moduleDirectory, StringComparison.Ordinal))
            return false; // named directly, not folded - the specifier already names this exact file

        var extensionLength = module.IsDeclaration ? FileManager.DeclarationExtension.Length : FileManager.LoomExtension.Length;
        var stem = Path.GetFileName(module.AbsolutePath)[..^extensionLength];
        return stem == SecondaryIndexFileName;
    }

    private static string PrefixDot(string specifier) =>
        specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal) ? specifier : "./" + specifier;

    private ModuleResolution ResolveRelative(SourceFile importingFile, string specifier)
    {
        var importingDirectory = Path.GetDirectoryName(Path.GetFullPath(importingFile.AbsolutePath));
        if (importingDirectory == null)
            return ModuleResolution.Failed(ModuleResolutionStatus.NotFound);

        var basePath = Path.GetFullPath(Path.Combine(importingDirectory, specifier));
        return _roots.Of(importingFile).Contains(basePath)
            ? ResolveAt(importingFile, basePath)
            : ModuleResolution.Failed(ModuleResolutionStatus.OutsideSourceDirectory);
    }

    /// <summary>
    ///     Resolves <c>"pkg"</c> or <c>"pkg/nested/module"</c> against the root publishing <c>pkg</c>: the bare
    ///     package names that root's entry module, <c>init.loom</c> at the top of its source directory, and
    ///     anything after the package name is a path within that same source directory.
    /// </summary>
    /// <remarks>
    ///     A package is only importable by the projects that actually declare it, so that a package reachable
    ///     only because something else in the build depends on it cannot be imported by accident. The root's
    ///     own files are exempt: a package always refers to itself by name.
    /// </remarks>
    private ModuleResolution ResolvePackage(SourceFile importingFile, string specifier)
    {
        if (!TrySplitPackageSpecifier(specifier, out var package, out var subpath))
            return ModuleResolution.Failed(ModuleResolutionStatus.UnsupportedSpecifier);

        var root = _roots.WithPackage(package);
        if (root == null)
            return ModuleResolution.Failed(ModuleResolutionStatus.PackageNotFound, package);

        var importingRoot = _roots.Of(importingFile);
        if (root != importingRoot && !importingRoot.Config.Dependencies.ContainsKey(package))
            return ModuleResolution.Failed(ModuleResolutionStatus.UndeclaredDependency, package);

        if (subpath.Length == 0)
            return ResolveAt(importingFile, root.SourceDirectory, package, indexOnly: true);

        var basePath = Path.GetFullPath(Path.Combine(root.SourceDirectory, subpath));
        return root.Contains(basePath)
            ? ResolveAt(importingFile, basePath, package)
            : ModuleResolution.Failed(ModuleResolutionStatus.OutsideSourceDirectory, package);
    }

    /// <param name="package"></param>
    /// <param name="indexOnly">
    ///     Set when <paramref name="basePath" /> is a directory rather than a module path, as it is for a bare
    ///     package specifier: only the <c>init</c> file inside it can be meant, never a sibling file beside it.
    /// </param>
    /// <param name="importingFile"></param>
    /// <param name="basePath"></param>
    private ModuleResolution ResolveAt(SourceFile importingFile, string basePath, PackageName? package = null, bool indexOnly = false)
    {
        SourceFile? caseInsensitiveMatch = null;
        foreach (var candidate in GetCandidatePaths(basePath, indexOnly))
        {
            if (_modulesByPath.TryGetValue(candidate, out var file))
                return file == importingFile
                    ? ModuleResolution.Failed(ModuleResolutionStatus.SelfImport, package)
                    : ModuleResolution.Resolved(file);

            caseInsensitiveMatch ??= _modulesByPathIgnoringCase.GetValueOrDefault(candidate);
        }

        return ModuleResolution.NotFound(caseInsensitiveMatch, package);
    }

    /// <summary>
    ///     Splits a bare specifier into the package it names and the path within that package's source
    ///     directory. <c>"scope/name/module"</c> is ambiguous — a scoped package with a module under it, or an
    ///     unscoped one with two path segments — so the scoped reading wins when a root actually publishes that
    ///     scoped name, and the unscoped reading is what an unresolvable specifier is reported as, that being
    ///     the far likelier thing to have been meant.
    /// </summary>
    private bool TrySplitPackageSpecifier(string specifier, [NotNullWhen(true)] out PackageName? package, out string subpath)
    {
        package = null;
        subpath = "";

        var segments = specifier.Split('/');
        if (Array.Exists(segments, segment => segment.Length == 0))
            return false;

        for (var count = Math.Min(MaximumPackageSegments, segments.Length); count >= 1; count--)
        {
            if (!PackageName.TryParse(string.Join('/', segments.Take(count)), out var candidate))
                continue;

            package = candidate;
            subpath = string.Join('/', segments.Skip(count));
            if (_roots.WithPackage(candidate) != null)
                return true;
        }

        return package != null;
    }

    /// <param name="directory">The directory the path is written relative to.</param>
    /// <param name="module">The module the path names.</param>
    /// <param name="foldIndexFile">
    ///     Whether a folder-index file (<c>init</c> or <c>main</c>) folds into its folder, as it does for a
    ///     specifier a person writes. Off for <see cref="LiteralRequirePath" />, which names the file Luau
    ///     itself has to find without any folding it does not already know how to do.
    /// </param>
    /// <returns>The path naming <paramref name="module" /> from <paramref name="directory" />, extension-less and <c>/</c>-separated.</returns>
    private static string ModulePath(string directory, SourceFile module, bool foldIndexFile = true)
    {
        var relativePath = Path.GetRelativePath(directory, Path.GetFullPath(module.AbsolutePath));
        var extensionLength = module.IsDeclaration ? FileManager.DeclarationExtension.Length : FileManager.LoomExtension.Length;
        var withoutExtension = relativePath[..^extensionLength];
        if (foldIndexFile && IsIndexFileName(Path.GetFileName(withoutExtension)))
            withoutExtension = Path.GetDirectoryName(withoutExtension) ?? withoutExtension;

        return withoutExtension == "." ? "" : withoutExtension.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool IsIndexFileName(string name) => name is IndexFileName or SecondaryIndexFileName;

    /// <remarks>
    ///     A <c>.loom</c> candidate is tried before its <c>.d.loom</c> counterpart at the same base path, so a
    ///     project that somehow has both is not ambiguous - the compiled module wins, the same way it would if
    ///     the two just happened to be tried in directory order. <c>init</c> is tried before <c>main</c> for the
    ///     same reason, so a folder carrying both resolves to the Rojo-native one rather than being ambiguous.
    /// </remarks>
    private static IEnumerable<string> GetCandidatePaths(string basePath, bool indexOnly)
    {
        if (!indexOnly)
        {
            yield return basePath + FileManager.LoomExtension;
            yield return basePath + FileManager.DeclarationExtension;
        }

        yield return Path.Combine(basePath, IndexFileName + FileManager.LoomExtension);
        yield return Path.Combine(basePath, IndexFileName + FileManager.DeclarationExtension);
        yield return Path.Combine(basePath, SecondaryIndexFileName + FileManager.LoomExtension);
        yield return Path.Combine(basePath, SecondaryIndexFileName + FileManager.DeclarationExtension);
    }
}
