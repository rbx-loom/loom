using System.Reflection;
using Loom.Config;
using Loom.Core.Text;

namespace Loom.Core.Pipeline;

public static class FileManager
{
    public const string LoomExtension = ".loom";
    public const string DeclarationExtension = ".d.loom";
    private const string LuauExtension = ".luau";
    public const string IncludeFolderName = "include";
    private const string IncludeResourcePrefix = "Include/";

    private static readonly Assembly _resourceAssembly = typeof(FileManager).Assembly;

    /// <summary>
    ///     Whether a project of this type gets an include folder. A library's output runs against the runtime of
    ///     whichever project consumes it, so one of its own would only ever be dead weight in the package.
    /// </summary>
    public static bool HasIncludeFolder(ProjectType projectType) => projectType != ProjectType.Library;

    /// <summary>Writes the include folder for <paramref name="config" />'s project, unless its type has none.</summary>
    /// <returns>Whether the folder was written.</returns>
    public static bool WriteIncludeFolder(LoomConfig config)
    {
        if (!HasIncludeFolder(config.ProjectType))
            return false;

        WriteIncludeFolder(config.ProjectDirectory);
        return true;
    }

    /// <summary>
    ///     Writes the bundled Luau runtime support into the project's include folder, overwriting whatever is
    ///     already there. The sources are embedded resources rather than files beside the executable: the compiler
    ///     is installed as a lone binary by a toolchain manager, so nothing may be assumed to sit next to it.
    /// </summary>
    public static void WriteIncludeFolder(string projectDirectory)
    {
        var destination = Path.Combine(projectDirectory, IncludeFolderName);
        Directory.CreateDirectory(destination);

        foreach (var resourceName in _resourceAssembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(IncludeResourcePrefix, StringComparison.Ordinal)) continue;

            var relativePath = resourceName[IncludeResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var destinationFile = Path.Combine(destination, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

            using var stream = _resourceAssembly.GetManifestResourceStream(resourceName)!;
            using var file = File.Create(destinationFile);
            stream.CopyTo(file);
        }
    }

    /// <summary>Writes the file's rendered Luau, skipping the write entirely when it would be byte-identical to what's already on disk.</summary>
    /// <returns>Whether the file was actually written.</returns>
    public static bool WriteCompiledFile(CompiledFile file)
    {
        if (File.Exists(file.Path) && File.ReadAllText(file.Path) == file.RenderedLuau)
            return false;

        var directory = Path.GetDirectoryName(file.Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(file.Path, file.RenderedLuau);
        Log.Info($"Wrote {file.Path}");
        return true;
    }

    public static string GetOutputPath(SourceFile file, LoomConfig config)
    {
        var relativePath = Path.GetRelativePath(config.Files.SourceDirectory, file.AbsolutePath);
        var outputPath = Path.Combine(config.Files.OutputDirectory, relativePath);
        return WithLuauExtension(outputPath);
    }

    /// <summary>
    ///     Swaps a source path's extension for <c>.luau</c>. A <c>.d.loom</c> file's own name is what loses
    ///     the extension - <c>jecs.d.loom</c> becomes <c>jecs.luau</c>, not <c>jecs.d.luau</c> - since that is
    ///     the name its hand-written runtime sibling already has (see <see cref="CopyDeclarationRuntime" />),
    ///     and a require of one has to find the other under the one path.
    /// </summary>
    public static string WithLuauExtension(string path) =>
        path.EndsWith(DeclarationExtension, StringComparison.Ordinal)
            ? string.Concat(path.AsSpan(0, path.Length - DeclarationExtension.Length), LuauExtension)
            : Path.ChangeExtension(path, LuauExtension);

    /// <summary>The hand-written Luau module a declaration file stands in for, if it has one - <c>jecs.luau</c> beside <c>jecs.d.loom</c>.</summary>
    public static string? RuntimeSiblingPath(SourceFile file) =>
        file.IsDeclaration ? string.Concat(file.AbsolutePath.AsSpan(0, file.AbsolutePath.Length - DeclarationExtension.Length), LuauExtension) : null;

    /// <summary>
    ///     Copies <paramref name="file" />'s runtime sibling into the output tree in its place, when it has
    ///     one. A declaration file emits nothing of its own - every <c>declare</c> vanishes entirely - so an
    ///     export of one names a value the compiler never generates; for a package like a native binding,
    ///     naming it is exactly the point, and the hand-written sibling is where the value actually lives.
    ///     A declaration file with no sibling - the ordinary case, ambient names for something that already
    ///     exists at runtime without a require - writes nothing, same as before this existed.
    /// </summary>
    /// <returns>Whether the file was actually written.</returns>
    public static bool CopyDeclarationRuntime(CompiledFile file)
    {
        if (RuntimeSiblingPath(file.SourceFile) is not { } sibling || !File.Exists(sibling))
            return false;

        if (File.Exists(file.Path) && File.ReadAllText(file.Path) == File.ReadAllText(sibling))
            return false;

        var directory = Path.GetDirectoryName(file.Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.Copy(sibling, file.Path, true);
        Log.Info($"Wrote {file.Path}");
        return true;
    }

    public static bool IsLoomFile(string path) => Path.GetExtension(path) == LoomExtension;

    public static SourceFile LoadSingle(string path) => new(Path.GetFullPath(path));

    public static List<SourceFile> LoadDirectory(string directoryPath) => LoadDirectory(directoryPath, SearchOption.AllDirectories);

    private static List<SourceFile> LoadDirectory(string directoryPath, SearchOption searchOption) =>
        !string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath)
            ? [.. Directory.GetFiles(directoryPath, $"*{LoomExtension}", searchOption).Select(LoadSingle)]
            : [];
}