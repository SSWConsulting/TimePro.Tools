using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Timesheets;

public class GetCommandJsonTests
{
    [Fact]
    public async Task Get_WithFromAndTo_LabelsTheRangeFromAndTo()
    {
        var (exitCode, stdout) = await RunAsync(["get", "--from", "2026-09-01", "--to", "2026-09-03", "--json"]);

        exitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        root.GetProperty("from").GetString().Should().Be("2026-09-01");
        root.GetProperty("to").GetString().Should().Be("2026-09-03");
        root.TryGetProperty("weekStart", out _).Should().BeFalse();
        root.TryGetProperty("weekEnd", out _).Should().BeFalse();
        root.GetProperty("days").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Get_WithWeek_KeepsWeekStartAndWeekEnd()
    {
        var (exitCode, stdout) = await RunAsync(["get", "--week", "--json"]);

        exitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        root.GetProperty("weekStart").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("weekEnd").GetString().Should().NotBeNullOrWhiteSpace();
        root.TryGetProperty("from", out _).Should().BeFalse();
        root.TryGetProperty("to", out _).Should().BeFalse();
    }

    private static async Task<(int ExitCode, string Stdout)> RunAsync(string[] args)
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetTimesheetsAsync("TST", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
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

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator => configurator.AddCommand<GetCommand>("get"));

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
