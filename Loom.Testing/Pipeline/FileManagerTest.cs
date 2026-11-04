using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.Testing.Pipeline;

[Collection("Assembly")]
public class FileManagerTest
{
    [Fact]
    public void Loads_Single()
    {
        var file = FileManager.LoadSingle($"{AssemblyFixture.Snapshots}/src/basic_binary.loom");
        Assert.Equal("basic_binary.loom", file.Name);
        Assert.Equal($"{AssemblyFixture.Snapshots}{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}basic_binary.loom", file.RelativePath());
        Assert.Equal($"src{Path.DirectorySeparatorChar}basic_binary.loom", file.RelativePath(AssemblyFixture.Snapshots));
        Assert.Equal("basic_binary.loom", file.RelativePath(AssemblyFixture.Snapshots + "/src"));
        Assert.Equal("1 + 2", file.SourceText);
    }

    [Fact]
    public void GetOutputPath_SourceDirectoryLeafRepeatedInPath_DoesNotCorruptPath()
    {
        var sourceDirectory = Path.Combine("proj", "src", "src");
        var outputDirectory = Path.Combine("proj", "src", "dist");
        var config = new LoomConfig { Files = new FilesConfig { SourceDirectory = sourceDirectory, OutputDirectory = outputDirectory } };
        var file = new SourceFile(Path.Combine(sourceDirectory, "foo.loom"), "");

        var outputPath = FileManager.GetOutputPath(file, config);
        Assert.Equal(Path.Combine(outputDirectory, "foo.luau"), outputPath);
    }

    [Fact]
    public void WriteCompiledFile_Writes_WhenContentDiffers() =>
        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (_, result) =>
            {
                var file = Assert.Single(result.Files);
                Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                File.WriteAllText(file.Path, "-- stale content");

                Assert.True(FileManager.WriteCompiledFile(file));
                Assert.Equal(file.RenderedLuau, File.ReadAllText(file.Path));
            }
        );

    [Fact]
    public void WriteIncludeFolder_WritesBundledRuntime_IntoFreshDirectory() =>
        WithTempDirectory(directory =>
            {
                FileManager.WriteIncludeFolder(directory);

                var include = Path.Combine(directory, "include");
                Assert.Contains("--!strict", File.ReadAllText(Path.Combine(include, "loom_runtime.luau")));
                Assert.Contains("export type Connection", File.ReadAllText(Path.Combine(include, "event.luau")));
            }
        );

    /// <summary>
    ///     The runtime ships as an embedded resource so that it survives being installed as a lone binary, which
    ///     means it must land in the project whatever sits beside the executable - here, nothing at all.
    /// </summary>
    [Fact]
    public void WriteIncludeFolder_DoesNotReadFilesBesideTheExecutable()
    {
        Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "include")));

        WithTempDirectory(directory =>
            {
                FileManager.WriteIncludeFolder(directory);
                Assert.NotEmpty(Directory.GetFiles(Path.Combine(directory, "include")));
            }
        );
    }

    [Fact]
    public void WriteIncludeFolder_OverwritesStaleContent_AndLeavesUnrelatedFiles() =>
        WithTempDirectory(directory =>
            {
                var include = Path.Combine(directory, "include");
                Directory.CreateDirectory(include);
                File.WriteAllText(Path.Combine(include, "loom_runtime.luau"), "-- stale content");
                File.WriteAllText(Path.Combine(include, "vendored.luau"), "-- not ours");

                FileManager.WriteIncludeFolder(directory);

                Assert.DoesNotContain("stale", File.ReadAllText(Path.Combine(include, "loom_runtime.luau")));
                Assert.Equal("-- not ours", File.ReadAllText(Path.Combine(include, "vendored.luau")));
            }
        );

    [Theory]
    [InlineData(ProjectType.Game, true)]
    [InlineData(ProjectType.Plugin, true)]
    [InlineData(ProjectType.Library, false)]
    public void WriteIncludeFolder_WritesOnlyForProjectTypesThatRunTheRuntime(ProjectType projectType, bool written) =>
        WithTempDirectory(directory =>
            {
                var config = new LoomConfig { ProjectDirectory = directory, ProjectType = projectType };

                Assert.Equal(written, FileManager.WriteIncludeFolder(config));
                Assert.Equal(written, Directory.Exists(Path.Combine(directory, FileManager.IncludeFolderName)));
            }
        );

    [Fact]
    public void WriteCompiledFile_SkipsWrite_WhenContentUnchanged() =>
        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (_, result) =>
            {
                var file = Assert.Single(result.Files);
                Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                File.WriteAllText(file.Path, file.RenderedLuau);
                var writtenAt = File.GetLastWriteTimeUtc(file.Path);

                Assert.False(FileManager.WriteCompiledFile(file));
                Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(file.Path));
            }
        );

    private static void WithTempDirectory(Action<string> assert)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-include-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            assert(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}