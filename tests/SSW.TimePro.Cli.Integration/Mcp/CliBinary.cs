namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Locates the built CLI assembly so stdio tests can launch the real <c>tp mcp</c> entry point as a
/// child process.
///
/// The path is derived exactly from the running test assembly's own configuration and target
/// framework, never searched for: picking the newest match under <c>bin</c> would happily run a
/// Release build, or a stale <c>publish</c> output, while the tests under inspection are Debug.
/// </summary>
public static class CliBinary
{
    private const string AssemblyName = "SSW.TimePro.Cli.dll";

    private static readonly Lazy<string> Located = new(Locate);

    public static string Path => Located.Value;

    private static string Locate()
    {
        // .../tests/SSW.TimePro.Cli.Integration/bin/<Configuration>/<TargetFramework>/
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        var targetFramework = testOutput.Name;
        var configuration = testOutput.Parent?.Name
            ?? throw new InvalidOperationException(
                $"Cannot derive the build configuration from {AppContext.BaseDirectory}.");

        var expected = System.IO.Path.Combine(
            FindRepoRoot(), "src", "SSW.TimePro.Cli", "bin", configuration, targetFramework, AssemblyName);

        if (!File.Exists(expected))
            throw new InvalidOperationException(
                $"Expected the CLI at {expected}. Run 'dotnet build -c {configuration}' first.");

        // Without this the assembly cannot be launched with `dotnet <dll>`.
        var runtimeConfig = System.IO.Path.ChangeExtension(expected, ".runtimeconfig.json");
        if (!File.Exists(runtimeConfig))
            throw new InvalidOperationException($"Expected a runtime config beside {expected}.");

        return expected;
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
