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

public class UpdateCommandTests
{
    [Theory]
    [InlineData(120, 2.0)]
    [InlineData(30, 0.5)]
    [InlineData(0, 0.0)]
    public async Task Update_WhenLessIsSpecified_ConvertsMinutesToApiHours(
        int lessMinutes,
        double expectedHours)
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingTimesheet(api, lessHours: 1);
        TimesheetRequest? request = null;
        api.UpdateTimesheetAsync(
                Arg.Do<TimesheetRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new TimesheetResponse { Success = true });
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            "42",
            "--date", "2026-03-16",
            "--less", lessMinutes.ToString(),
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.TimeLess.Should().Be((decimal)expectedHours);
        request.TimeStart.Should().Be("2026-03-16T09:00:00");
        request.TimeEnd.Should().Be("2026-03-16T18:00:00");
        request.Note.Should().Be("Product search");
        request.CategoryId.Should().Be("WEBDEV");
        request.SellPrice.Should().Be(175m);
    }

    [Fact]
    public async Task Update_WhenLessIsOmitted_PreservesExistingHours()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingTimesheet(api, lessHours: 1.25m);
        TimesheetRequest? request = null;
        api.UpdateTimesheetAsync(
                Arg.Do<TimesheetRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new TimesheetResponse { Success = true });
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            "42",
            "--date", "2026-03-16",
            "--description", "Updated product search",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.TimeLess.Should().Be(1.25m);
    }

    [Fact]
    public async Task Update_WhenLessIsNegative_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            "42",
            "--less", "-1",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        await api.DidNotReceive().GetTimesheetsAsync(
            Arg.Any<string>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        await api.DidNotReceive().UpdateTimesheetAsync(
            Arg.Any<TimesheetRequest>(),
            Arg.Any<CancellationToken>());
    }

    private static void ConfigureExistingTimesheet(ITimeProApiClient api, decimal lessHours)
    {
        api.GetTimesheetsAsync(
                "TST",
                new DateOnly(2026, 3, 16),
                Arg.Any<CancellationToken>())
            .Returns([
                new TimesheetItem
                {
                    TimeId = 42,
                    EmpId = "TST",
                    ClientId = "NWIND",
                    ProjectId = "1I776Q",
                    IterationId = 3402,
                    LocationId = "Home",
                    Notes = "Product search",
                    Date = "2026-03-16T00:00:00",
                    StartTime = "2026-03-16T09:00:00",
                    EndTime = "2026-03-16T18:00:00",
                    BillableId = "B",
                    Less = lessHours
                }
            ]);
        api.QueryTimesheetsAsync(
                Arg.Any<TimesheetSummaryFilter>(),
                Arg.Any<CancellationToken>())
            .Returns([
                new TimesheetSummaryEntry
                {
                    TimeId = 42,
                    CategoryId = "WEBDEV",
                    SellPrice = 175m
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
        app.Configure(configurator =>
        {
            configurator.AddCommand<UpdateCommand>("update");
        });
        return app;
    }
}
