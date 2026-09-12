using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class CommandLineErrorHandlerTests
{
    [Theory]
    [InlineData(true, "ts", "get", "--json")]
    [InlineData(true, "--json", "ts", "get")]
    [InlineData(true, "ts", "get", "--json=true")]
    [InlineData(false, "ts", "get", "--json=false")]
    [InlineData(false, "ts", "get")]
    [InlineData(false, "ts", "get", "--jsonish")]
    [InlineData(false)]
    public void IsJsonRequested_DetectsTheFlagAnywhereInArgv(bool expected, params string[] args)
    {
        CommandLineErrorHandler.IsJsonRequested(args).Should().Be(expected);
    }

    [Fact]
    public async Task Run_WhenCommandIsUnknownAndJsonRequested_WritesEnvelopeToStdout()
    {
        var (exitCode, stdout) = await RunAsync(["ts", "list", "--json"]);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().Should().Contain("Unknown command");
        error.GetProperty("code").ValueKind.Should().Be(JsonValueKind.Null);
        error.GetProperty("detail").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Run_WhenArgumentCannotBeBoundAndJsonRequested_WritesEnvelopeToStdout()
    {
        var (exitCode, stdout) = await RunAsync(["ts", "update", "not-a-number", "--json"]);

        exitCode.Should().Be(1, stdout);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Run_WhenJsonIsExplicitlyDisabled_LeavesStdoutEmpty()
    {
        var (exitCode, stdout) = await RunAsync(["ts", "list", "--json=false"]);

        exitCode.Should().Be(1);
        stdout.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_WhenCommandIsUnknownWithoutJson_LeavesStdoutEmpty()
    {
        var (exitCode, stdout) = await RunAsync(["ts", "list"]);

        exitCode.Should().Be(1);
        stdout.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_WhenLessIsFractionalAndJsonRequested_ExplainsTheExpectedUnit()
    {
        var api = Substitute.For<ITimeProApiClient>();

        var (exitCode, stdout) = await RunAsync(["ts", "update", "42", "--less", "1.5", "--json"], api);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Be("--less expects whole minutes, e.g. --less 90 (got '1.5')");
        await api.DidNotReceive().UpdateTimesheetAsync(
            Arg.Any<SSW.TimePro.Cli.Shared.Models.TimesheetRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WhenCommandIsUnknownAndJsonRequested_StillReportsTheParseError()
    {
        var (exitCode, stdout) = await RunAsync(["nosuchcommand", "--json"]);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().Should().Contain("Unknown command");
        error.TryGetProperty("tenant", out _).Should().BeFalse();
        error.TryGetProperty("apiUrl", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Run_WhenTheApiIsUnreachableAndJsonRequested_NamesTheTenantInTheEnvelope()
    {
        var (exitCode, stdout) = await RunAsync(["ts", "get", "--json"], UnreachableApi());

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().Should().Be("Connection refused (localhost:1)");
        error.GetProperty("tenant").GetString().Should().Be("northwind-local");
        error.GetProperty("apiUrl").GetString().Should().Be("https://localhost:1/");
        error.GetProperty("detail").GetString().Should().Contain("tp tenant set");
    }

    [Fact]
    public async Task Run_WhenTheApiIsUnreachableWithoutJson_LeavesStdoutEmpty()
    {
        var (exitCode, stdout) = await RunAsync(["ts", "get"], UnreachableApi());

        exitCode.Should().Be(1);
        stdout.Should().BeEmpty();
    }

    private static ITimeProApiClient UnreachableApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetTimesheetsAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns<List<SSW.TimePro.Cli.Shared.Models.TimesheetItem>>(_ => throw new TimeProConnectionException(
                "Connection refused (localhost:1)",
                tenantFile: "northwind-local",
                tenantId: "northwind",
                apiUrl: "https://localhost:1/"));

        return api;
    }

    private static async Task<(int ExitCode, string Stdout)> RunAsync(
        string[] args,
        ITimeProApiClient? api = null)
    {
        api ??= Substitute.For<ITimeProApiClient>();
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });

        var services = new ServiceCollection();
        services.AddSingleton(api);
        services.AddSingleton(config);
        services.AddSingleton<TimesheetUpdateService>();

        var jsonRequested = CommandLineErrorHandler.IsJsonRequested(args);
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator =>
        {
            configurator.SetExceptionHandler((ex, _) => CommandLineErrorHandler.Handle(ex, jsonRequested));
            configurator.AddBranch("ts", ts =>
            {
                ts.AddCommand<UpdateCommand>("update");
                ts.AddCommand<GetCommand>("get");
            });
        });

        var original = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            var exitCode = await app.RunAsync(args, TestContext.Current.CancellationToken);
            return (exitCode, writer.ToString().Trim());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
