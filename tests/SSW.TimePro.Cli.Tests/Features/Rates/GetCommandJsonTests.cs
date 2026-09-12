using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console.Cli;
using Xunit;

using RateGet = SSW.TimePro.Cli.Features.Rates.GetCommand;

namespace SSW.TimePro.Cli.Tests.Features.Rates;

public class GetCommandJsonTests
{
    private static readonly string[] SharedKeys =
    [
        "found", "clientId", "date", "empId", "employeeName", "clientName",
        "rate", "prepaidRate", "clientRateId", "expiryDate", "notes"
    ];

    [Fact]
    public async Task Get_WhenRateIsMissing_EmitsFullSchemaWithNullRateFields()
    {
        var (exitCode, stdout) = await RunAsync(rate: null);

        exitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(SharedKeys);
        root.GetProperty("found").GetBoolean().Should().BeFalse();
        root.GetProperty("clientId").GetString().Should().Be("NWIND");
        root.GetProperty("date").GetString().Should().Be("2026-03-16");
        root.GetProperty("rate").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("clientName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_WhenRateExists_EmitsTheSameSchemaWithFoundTrue()
    {
        var (exitCode, stdout) = await RunAsync(rate: new ClientRateResponse
        {
            EmpId = "BOB",
            ClientId = "NWIND",
            ClientName = "Northwind Traders",
            EmployeeName = "Bob Northwind",
            Rate = 175m,
            ClientRateId = 7
        });

        exitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(SharedKeys);
        root.GetProperty("found").GetBoolean().Should().BeTrue();
        root.GetProperty("date").GetString().Should().Be("2026-03-16");
        root.GetProperty("rate").GetDecimal().Should().Be(175m);
        root.GetProperty("clientRateId").GetInt32().Should().Be(7);
        root.GetProperty("prepaidRate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task<(int ExitCode, string Stdout)> RunAsync(ClientRateResponse? rate)
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetClientRateAsync("TST", "NWIND", new DateOnly(2026, 3, 16), Arg.Any<CancellationToken>())
            .Returns(rate);

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
        app.Configure(configurator => configurator.AddCommand<RateGet>("get"));

        var original = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            var exitCode = await app.RunAsync(
                ["get", "--client", "NWIND", "--date", "2026-03-16", "--json"],
                TestContext.Current.CancellationToken);
            return (exitCode, writer.ToString().Trim());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
