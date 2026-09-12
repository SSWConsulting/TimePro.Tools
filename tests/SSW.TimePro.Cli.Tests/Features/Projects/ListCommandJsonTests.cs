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

using ProjectList = SSW.TimePro.Cli.Features.Projects.ListCommand;

namespace SSW.TimePro.Cli.Tests.Features.Projects;

public class ListCommandJsonTests
{
    [Fact]
    public async Task List_DropsTheDropdownPlaceholderRow()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetProjectsForClientAsync("TST", "NWIND", Arg.Any<CancellationToken>())
            .Returns([
                new ProjectForSelect { Value = null, DisplayText = "Empty - Please add the project" },
                new ProjectForSelect { Value = "   ", DisplayText = "Whitespace id" },
                new ProjectForSelect { Value = "1I776Q", DisplayText = "Northwind Traders", UseIteration = true }
            ]);

        var (exitCode, stdout) = await RunAsync(api);

        exitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(stdout);
        var rows = doc.RootElement.EnumerateArray().ToList();

        rows.Should().HaveCount(1);
        rows[0].GetProperty("value").GetString().Should().Be("1I776Q");
    }

    private static async Task<(int ExitCode, string Stdout)> RunAsync(ITimeProApiClient api)
    {
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
        app.Configure(configurator => configurator.AddCommand<ProjectList>("list"));

        var original = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            var exitCode = await app.RunAsync(
                ["list", "--client", "NWIND", "--json"],
                TestContext.Current.CancellationToken);
            return (exitCode, writer.ToString().Trim());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
