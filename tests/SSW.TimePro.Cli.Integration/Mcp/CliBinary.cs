namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Locates the built CLI assembly so stdio tests can launch the real
/// <c>tp mcp</c> entry point as a child process.
/// </summary>
public static class CliBinary
{
    private const string AssemblyName = "SSW.TimePro.Cli.dll";

    private static readonly Lazy<string> Located = new(Locate);

    public static string Path => Located.Value;

    private static string Locate()
    {
        var repoRoot = FindRepoRoot();
        var projectBin = System.IO.Path.Combine(repoRoot, "src", "SSW.TimePro.Cli", "bin");

        // The test project has a ProjectReference to the CLI, so building the tests always
        // produces this assembly alongside its runtimeconfig.json.
        var candidates = Directory.Exists(projectBin)
            ? Directory.GetFiles(projectBin, AssemblyName, SearchOption.AllDirectories)
                .Where(p => File.Exists(System.IO.Path.ChangeExtension(p, ".runtimeconfig.json")))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList()
            : [];

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"Could not find a runnable {AssemblyName} under {projectBin}. Run 'dotnet build' first.");

        return candidates[0];
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Directory.Build.props")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find the repository root above {AppContext.BaseDirectory}.");
    }
}
