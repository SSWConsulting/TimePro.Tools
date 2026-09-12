using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Cli;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

/// <summary>
/// Runs the real command tree, because strict parsing is a property of the app configuration.
/// </summary>
public class StrictOptionParsingTests
{
    [Fact]
    public async Task Run_WhenAnOptionIsUnknown_Fails()
    {
        var (exitCode, stdout, api) = await RunAsync(["ts", "get", "--week", "--nope"]);

        exitCode.Should().Be(1);
        stdout.Should().BeEmpty();
        await api.DidNotReceive().GetTimesheetsAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WhenAnOptionIsUnknownAndJsonRequested_NamesTheOptionInTheEnvelope()
    {
        var (exitCode, stdout, _) = await RunAsync(["ts", "get", "--week", "--json", "--nope"]);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().Should().Contain("nope");
        error.GetProperty("code").ValueKind.Should().Be(JsonValueKind.Null);
        error.GetProperty("detail").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Run_WhenARealOptionIsMistyped_DoesNotWrite()
    {
        var (exitCode, stdout, api) = await RunAsync(["ts", "update", "42", "--iteraton", "Sprint 5", "--json"]);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("error").GetProperty("message").GetString().Should().Contain("iteraton");
        await api.DidNotReceive().UpdateTimesheetAsync(Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WhenTheCommandLacksAJsonOption_StillUsesTheEnvelope()
    {
        var (exitCode, stdout, _) = await RunAsync(["skills", "create", ".claude", "--json"]);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("error").GetProperty("message").GetString().Should().Contain("json");
    }

    [Fact]
    public async Task Run_WhenACommandIsUnknown_KeepsTheDidYouMeanHint()
    {
        var (exitCode, stdout, _) = await RunAsync(["ts", "gett", "--json"]);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().Should().Contain("Unknown command");
        error.GetProperty("detail").GetString().Should().Contain("Did you mean 'get'?");
    }

    [Fact]
    public async Task Run_WhenEveryOptionIsKnown_Succeeds()
    {
        var (exitCode, stdout, _) = await RunAsync(["ts", "get", "--week", "--json"]);

        exitCode.Should().Be(0, stdout);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("weekStart").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Run_WhenAFlagCarriesItsOptionalValue_StillParses()
    {
        var (exitCode, stdout, api) = await RunAsync(["ts", "get", "--week", "1", "--json"]);

        exitCode.Should().Be(0, stdout);
        await api.Received().GetTimesheetsAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    private static async Task<(int ExitCode, string Stdout, ITimeProApiClient Api)> RunAsync(string[] args)
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetTimesheetsAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);

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
            configurator.SetExceptionHandler((ex, _) => CommandLineErrorHandler.Handle(ex, jsonRequested, args));
            CliConfiguration.Configure(configurator);
        });

        var original = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            var exitCode = await app.RunAsync(args, TestContext.Current.CancellationToken);
            return (exitCode, writer.ToString().Trim(), api);
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
