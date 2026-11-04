using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;

namespace Loom.CLI;

internal static class Scaffolder
{
    private static readonly IReadOnlyList<PromptOption<ProjectType>> _projectTypeOptions =
    [
        new(ProjectType.Game, "Game", "Scripts compiled straight into a Rojo tree."),
        new(ProjectType.Library, "Library", "A package other Loom projects can depend on."),
        new(ProjectType.Plugin, "Plugin", "A Roblox Studio plugin.")
    ];

    public static int NewProject(string directory)
    {
        var projectDirectory = Path.GetFullPath(directory);
        if (File.Exists(Path.Combine(projectDirectory, ConfigReader.ConfigFileName)))
        {
            Log.Fatal($"a Loom project already exists in '{projectDirectory}'.");
            return 1;
        }

        var name = Path.GetFileName(projectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Console.WriteLine($"{Colors.Bold}{Colors.Pink}Let's create a new Loom project!{Colors.Reset}{Colors.Dim} ({name}){Colors.Reset}");
        Console.WriteLine();

        var projectType = Prompt.Select("What kind of project would you like to create?", _projectTypeOptions);
        Console.WriteLine();
        var initializeGit = Prompt.Confirm("Initialize a git repository?");
        Console.WriteLine();

        Scaffold(projectDirectory, projectType, name);
        if (initializeGit)
            InitializeGitRepository(projectDirectory);

        Log.Info($"Created {Colors.Bold}{Colors.White}{name}{Colors.Reset}. Next steps:");
        Console.WriteLine($"  {Colors.Dim}cd{Colors.Reset} {directory}");
        Console.WriteLine($"  {Colors.Dim}loom{Colors.Reset} build");
        return 0;
    }

    private static void Scaffold(string projectDirectory, ProjectType projectType, string projectName)
    {
        Directory.CreateDirectory(projectDirectory);
        var sourceDirectory = Path.Combine(projectDirectory, "src");
        Directory.CreateDirectory(sourceDirectory);

        File.WriteAllText(
            Path.Combine(projectDirectory, ConfigReader.ConfigFileName),
            GetConfigContent(projectType)
        );

        var filesWritten = new List<string> { ".gitignore", "loom-config.toml" };
        if (projectType == ProjectType.Game)
        {
            const string projectFileName = "default.project.json";
            File.WriteAllText(
                Path.Combine(projectDirectory, projectFileName),
                GetRojoProjectContent(projectName)
            );
            filesWritten.Add(projectFileName);

            foreach (var realm in new[] { "client", "server", "shared" })
            {
                var realmDirectory = Path.Combine(sourceDirectory, realm);
                Directory.CreateDirectory(realmDirectory);
                File.WriteAllText(Path.Combine(realmDirectory, "main.loom"), StarterSource(projectType, realm));
                filesWritten.Add($"src/{realm}/main.loom");
            }
        }
        else
        {
            File.WriteAllText(Path.Combine(sourceDirectory, "main.loom"), StarterSource(projectType, null));
            filesWritten.Add("src/main.loom");
        }

        // packages are installed by a package manager and pinned by loom-lock.toml, which is committed instead;
        // the include folder is rewritten from the compiler on every build
        var ignored = new List<string> { "dist/", FilesConfig.PackagesDirectoryName + "/" };
        if (FileManager.HasIncludeFolder(projectType))
            ignored.Add(FileManager.IncludeFolderName + "/");

        File.WriteAllText(
            Path.Combine(projectDirectory, ".gitignore"),
            string.Concat(ignored.Select(entry => entry + Environment.NewLine))
        );
        Log.Info($"Wrote {string.Join(", ", filesWritten.SkipLast(1).Select(colorFileName))}, and {colorFileName(filesWritten.Last())}.");

        return;

        static string colorFileName(string fileName) => $"{Colors.Cyan}{fileName}{Colors.Reset}";
    }

    private static string GetConfigContent(ProjectType projectType) =>
        $"""
        project_type = "{ProjectTypeName(projectType)}"

        [files]
        source_directory = "src"
        output_directory = "dist"
        """
        + Environment.NewLine
        + (projectType == ProjectType.Game
            ? Environment.NewLine
              + """
                [realms]
                client = "client"
                server = "server"
                """
              + Environment.NewLine
            : "");

    /// <remarks>
    ///     The name goes through the JSON serializer rather than straight into the template: a directory may
    ///     legally be named with a quote or a backslash, and either one written verbatim produces a manifest
    ///     that nothing can read back.
    ///     <para>
    ///         <c>dist/packages</c> is mapped for the same reason the realms are: a dependency's output is written
    ///         there, and an import of one resolves to a require path through this file — so a project scaffolded
    ///         without it compiles until the day it depends on something, and then cannot. The realms are three
    ///         separate paths and packages are shared by all of them, which is what puts them under
    ///         ReplicatedStorage beside <c>include</c>.
    ///     </para>
    /// </remarks>
    private static string GetRojoProjectContent(string projectName) =>
        $$"""
        {
          "name": {{JsonSerializer.Serialize(projectName, ScaffolderJsonContext.Default.String)}},
          "globIgnorePaths": ["**/loom-config.toml"],
          "tree": {
            "$className": "DataModel",
            "ReplicatedStorage": {
              "$className": "ReplicatedStorage",
              "$path": "dist/shared",
              "include": {
                "$path": "include"
              },
              "packages": {
                "$path": "dist/packages"
              }
            },
            "ServerScriptService": {
              "$className": "ServerScriptService",
              "$path": "dist/server"
            },
            "StarterPlayer": {
              "$className": "StarterPlayer",
              "StarterPlayerScripts": {
                "$className": "StarterPlayerScripts",
                "$path": "dist/client"
              }
            }
          }
        }
        """;

    private static string ProjectTypeName(ProjectType projectType) =>
        projectType switch
        {
            ProjectType.Game => "game",
            ProjectType.Library => "library",
            ProjectType.Plugin => "plugin",
            _ => throw new ArgumentOutOfRangeException(nameof(projectType))
        };

    private static string StarterSource(ProjectType projectType, string? realm) =>
        (projectType, realm) switch
        {
            (ProjectType.Library, _) => "export fn hello(): string -> \"Hello from your Loom library!\";",
            (ProjectType.Plugin, _) => "print(\"Hello from your Loom plugin!\");",
            (ProjectType.Game, "client") => "print(\"Hello from the client!\");",
            (ProjectType.Game, "server") => "print(\"Hello from the server!\");",
            (ProjectType.Game, "shared") => "export let hello = \"Hello from shared code!\";",
            _ => "print(\"Hello from your Loom game!\");"
        };

    private static void InitializeGitRepository(string projectDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git", "init")
            {
                WorkingDirectory = projectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit();

            if (process?.ExitCode == 0)
                Log.Info("Initialized a git repository.");
            else
                Log.Warn("'git init' failed; is git installed and on PATH? The project was still created.");
        }
        catch (Win32Exception exception)
        {
            Log.Warn($"could not run 'git init': {exception.Message} The project was still created.");
        }
    }
}

[JsonSerializable(typeof(string))]
internal sealed partial class ScaffolderJsonContext : JsonSerializerContext;
