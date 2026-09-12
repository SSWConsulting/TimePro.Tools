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

public class DeleteCommandTests
{
    [Fact]
    public async Task Delete_WhenEntryIsSuggested_FailsLocallyWithoutCallingTheApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureEntry(api, isSuggested: true);
        var app = CreateApp(api);

        var output = await UpdateCommandTests.CaptureStdoutAsync(() => app.RunAsync([
            "delete",
            "42",
            "--date", "2026-03-16",
            "--json"
        ], TestContext.Current.CancellationToken));

        output.ExitCode.Should().Be(1);
        await api.DidNotReceive().DeleteTimesheetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        using var doc = JsonDocument.Parse(output.Stdout);
        doc.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Be("Timesheet 42 is a suggestion and cannot be deleted. Accept it first: tp ts accept 42");
    }

    [Fact]
    public async Task Delete_WhenEntryIsReal_DeletesIt()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureEntry(api, isSuggested: false);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "delete",
            "42",
            "--date", "2026-03-16",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        await api.Received(1).DeleteTimesheetAsync(42, Arg.Any<CancellationToken>());
    }

    private static void ConfigureEntry(ITimeProApiClient api, bool isSuggested)
    {
        api.GetTimesheetsAsync("TST", new DateOnly(2026, 3, 16), Arg.Any<CancellationToken>())
            .Returns([
                new TimesheetItem
                {
                    TimeId = 42,
                    EmpId = "TST",
                    ClientId = "NWIND",
                    ProjectId = "1I776Q",
                    Date = "2026-03-16T00:00:00",
                    IsSuggested = isSuggested
                }
            ]);
    }

    private static CommandApp CreateApp(ITimeProApiClient api)
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
        app.Configure(configurator => configurator.AddCommand<DeleteCommand>("delete"));
        return app;
    }
}
