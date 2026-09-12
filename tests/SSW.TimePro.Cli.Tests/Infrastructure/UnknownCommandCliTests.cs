using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Output;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

/// <summary>
/// Runs the real CLI, because the suggestion has to survive the wiring in Program.cs and
/// land on the right stream. The child gets a throwaway home so it cannot touch real config.
/// </summary>
public class UnknownCommandCliTests
{
    [Fact]
    public async Task UnknownSubcommand_HumanPath_ListsTheValidCommandsOnStderr()
    {
        var (exitCode, stdout, stderr) = await RunCliAsync(["ts", "list"]);

        exitCode.Should().Be(1);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("Commands: get, create, update");
    }

    [Fact]
    public async Task UnknownSubcommand_JsonPath_PutsTheSuggestionInDetail()
    {
        var (exitCode, stdout, _) = await RunCliAsync(["ts", "list", "--json"]);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().Should().Contain("Unknown command");
        error.GetProperty("detail").GetString().Should().Contain("Commands: get, create, update");
    }

    [Fact]
    public async Task MistypedSubcommand_JsonPath_SuggestsTheClosestCommand()
    {
        var (_, stdout, _) = await RunCliAsync(["leave", "balances", "stats", "--json"]);

        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("error").GetProperty("detail").GetString()
            .Should().Be("Did you mean 'status'? Commands: status, import");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(string[] args)
    {
        var cli = typeof(OutputHelper).Assembly.Location;
        var home = Directory.CreateTempSubdirectory("tp-cli-tests").FullName;

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(cli);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        startInfo.Environment["HOME"] = home;
        startInfo.Environment["USERPROFILE"] = home;
        startInfo.Environment["XDG_CONFIG_HOME"] = Path.Combine(home, ".config");

        try
        {
            using var process = Process.Start(startInfo)!;
            var stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            return (process.ExitCode, stdout.Trim(), stderr);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
