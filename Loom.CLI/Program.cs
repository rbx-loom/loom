using System.Text;
using Loom.CLI;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Packages;

var command = CliParser.Parse(args);
return command switch
{
    CliCommand.RunBuild build => compile(build.Options.Directory, build.Options.DependencyDiagnostics, watch: false),
    CliCommand.RunWatch watchCommand => compile(watchCommand.Options.Directory, watchCommand.Options.DependencyDiagnostics, watch: true),
    CliCommand.RunNew newCommand => Scaffolder.NewProject(newCommand.Options.Directory),
    CliCommand.RunAdd add => PackageCommands.Add(add.Options),
    CliCommand.RunPublish publish => PackageCommands.Publish(publish.Options),
    CliCommand.RunLogin login => AuthCommands.Login(login.Options),
    CliCommand.Done done => reportDone(done.ExitCode),
    _ => 1
};

static int reportDone(int exitCode)
{
    if (exitCode != 0)
        Log.Fatal("invalid command");

    return exitCode;
}

static int compile(string directory, bool dependencyDiagnostics, bool watch)
{
    Console.OutputEncoding = Encoding.UTF8;

    // a one-shot build has nothing left to do once a file fails, so it stops at the first error; a watch
    // has to stay up and report the next save, so it collects them like any other embedder does
    var diagnosticOptions = new DiagnosticOptions
    {
        OnFatalError = watch ? null : printAndExit, ReportDependencyDiagnostics = dependencyDiagnostics
    };

    if (!Projects.TryLocate(directory, out var config))
        return 1;

    FileManager.WriteIncludeFolder(config);
    if (!watch)
    {
        if (!PackageManager.Restore(config, out var restoreDiagnostics))
        {
            Projects.Report(restoreDiagnostics);
            return 1;
        }

        var roots = ProjectLoader.Load(config, out var projectDiagnostics);
        if (roots == null)
        {
            Projects.Report(projectDiagnostics);
            return 1;
        }

        var result = new CompilationUnit(roots, diagnosticOptions).Compile();
        Log.OutputResult(result);
        return result.Failed ? 1 : 0;
    }

    var watcher = new Watcher(diagnosticOptions);
    return watcher.Start(config);
}

static void printAndExit(Diagnostic diagnostic)
{
    Console.WriteLine(diagnostic.ToString());
    Environment.Exit(1);
}
