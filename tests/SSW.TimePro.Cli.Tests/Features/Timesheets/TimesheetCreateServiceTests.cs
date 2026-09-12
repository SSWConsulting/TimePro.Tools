using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Timesheets;

public class TimesheetCreateServiceTests
{
    private const string Emp = "TST";
    private const string Client = "NWIND";
    private const string Project = "1I776Q";
    private const string Date = "2026-03-16";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("B", 175)]
    [InlineData("BPP", 150)]
    [InlineData("W", 175)]
    public async Task Prepare_PricesTheEntryFromTheClientRate(string billable, int expected)
    {
        var api = ApiWithRate();
        var service = new TimesheetCreateService(api, Config());

        var prepared = await service.PrepareAsync(Emp, Options(Billable: billable), Ct);

        prepared.Plan!.Request.SellPrice.Should().Be(expected);
    }

    [Fact]
    public async Task Prepare_WhenTheRateHasExpired_ReportsNoActiveRateAndWritesNothing()
    {
        var api = ApiWithRate(expiry: "2026-01-31");
        var service = new TimesheetCreateService(api, Config());

        var prepared = await service.PrepareAsync(Emp, Options(), Ct);

        prepared.NoActiveRate.Should().BeTrue();
        prepared.Plan.Should().BeNull();
        await api.DidNotReceive().SaveClientRateAsync(
            Arg.Any<SaveClientRateModel>(), Arg.Any<CancellationToken>());
        await api.DidNotReceive().CreateTimesheetAsync(
            Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prepare_WhenTheClientHasNoRate_ReportsNoActiveRate()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetClientRateAsync(Emp, Client, new DateOnly(2026, 3, 16), Arg.Any<CancellationToken>())
            .Returns((ClientRateResponse?)null);
        var service = new TimesheetCreateService(api, Config());

        var prepared = await service.PrepareAsync(Emp, Options(), Ct);

        prepared.NoActiveRate.Should().BeTrue();
    }

    [Fact]
    public async Task Prepare_WhenASellPriceIsSupplied_SkipsTheRateLookup()
    {
        var api = ApiWithRate(expiry: "2026-01-31");
        var service = new TimesheetCreateService(api, Config());

        var prepared = await service.PrepareAsync(Emp, Options(SellPrice: 42m), Ct);

        prepared.Plan!.Request.SellPrice.Should().Be(42m);
        await api.DidNotReceive().GetClientRateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prepare_TakesTheCategoryFromTheRepoMapping()
    {
        var api = ApiWithRate();
        var service = new TimesheetCreateService(api, Config(mappedCategory: "WEBDEV"));

        var prepared = await service.PrepareAsync(Emp, Options(), Ct);

        prepared.Plan!.Request.CategoryId.Should().Be("WEBDEV");
        await api.DidNotReceive().QueryTimesheetsAsync(
            Arg.Any<TimesheetSummaryFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prepare_WithoutAMapping_TakesTheCategoryFromTheMostRecentEntry()
    {
        var api = ApiWithRate();
        api.QueryTimesheetsAsync(Arg.Any<TimesheetSummaryFilter>(), Arg.Any<CancellationToken>())
            .Returns([
                new TimesheetSummaryEntry { TimeId = 1, TimesheetDate = "2026-03-02", CategoryId = "TRAIN" },
                new TimesheetSummaryEntry { TimeId = 2, TimesheetDate = "2026-03-10", CategoryId = "WEBDEV" }
            ]);
        var service = new TimesheetCreateService(api, Config());

        var prepared = await service.PrepareAsync(Emp, Options(), Ct);

        prepared.Plan!.Request.CategoryId.Should().Be("WEBDEV");
    }

    [Fact]
    public async Task Prepare_PrefersTheExplicitCategory()
    {
        var api = ApiWithRate();
        var service = new TimesheetCreateService(api, Config(mappedCategory: "WEBDEV"));

        var prepared = await service.PrepareAsync(Emp, Options(Category: "TRAIN"), Ct);

        prepared.Plan!.Request.CategoryId.Should().Be("TRAIN");
    }

    [Fact]
    public async Task Prepare_ResolvesTheLocationAlias()
    {
        var api = ApiWithRate();
        var service = new TimesheetCreateService(api, Config());

        var prepared = await service.PrepareAsync(Emp, Options(Location: "At Client"), Ct);

        prepared.Plan!.Request.LocationId.Should().Be("Client");
    }

    [Theory]
    [InlineData("Monday", "Home")]
    [InlineData("Tuesday", "SSW")]
    public async Task Prepare_WithoutALocation_UsesTheWfhDefaultsForTheDay(string wfhDay, string expected)
    {
        var api = ApiWithRate();
        var config = Config();
        config.LoadGlobalConfig().Returns(new GlobalConfig { DefaultLocation = "Office", WfhDays = [wfhDay] });
        var service = new TimesheetCreateService(api, config);

        var prepared = await service.PrepareAsync(Emp, Options(), Ct);

        prepared.Plan!.Request.LocationId.Should().Be(expected);
    }

    [Theory]
    [InlineData(90, 1.5)]
    [InlineData(30, 0.5)]
    [InlineData(0, null)]
    [InlineData(null, null)]
    public async Task Prepare_SendsDeductedMinutesAsHours(int? minutes, double? expectedHours)
    {
        var api = ApiWithRate();
        var service = new TimesheetCreateService(api, Config());

        var prepared = await service.PrepareAsync(Emp, Options(Less: minutes), Ct);

        prepared.Plan!.Request.TimeLess.Should().Be((decimal?)expectedHours);
    }

    [Fact]
    public async Task Prepare_WhenDeductedMinutesAreNegative_Throws()
    {
        var service = new TimesheetCreateService(ApiWithRate(), Config());

        await Assert.ThrowsAsync<TimesheetValidationException>(
            () => service.PrepareAsync(Emp, Options(Less: -1), Ct));
    }

    [Fact]
    public async Task Prepare_DefaultsTheWorkdayTimes()
    {
        var api = ApiWithRate();
        var service = new TimesheetCreateService(api, Config());

        var prepared = await service.PrepareAsync(Emp, Options(), Ct);

        prepared.Plan!.Request.TimeStart.Should().Be("2026-03-16T09:00:00");
        prepared.Plan.Request.TimeEnd.Should().Be("2026-03-16T17:00:00");
        prepared.Plan.Request.DateCreated.Should().Be(Date);
        prepared.Plan.Request.TimeId.Should().BeNull();
    }

    [Fact]
    public async Task Apply_WhenTheApiAnswersWithAnEmptyBody_ReadsTheRowBackWithoutCreatingAgain()
    {
        var api = ApiWithRate();
        api.CreateTimesheetAsync(Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>())
            .Returns((TimesheetResponse?)null);
        api.GetTimesheetsAsync(Emp, new DateOnly(2026, 3, 16), Arg.Any<CancellationToken>())
            .Returns([CreatedRow(4242)]);
        var service = new TimesheetCreateService(api, Config());
        var prepared = await service.PrepareAsync(Emp, Options(Description: "Product search"), Ct);

        var result = await service.ApplyAsync(prepared.Plan!, Ct);

        result.Success.Should().BeTrue();
        result.TimesheetId.Should().Be(4242);
        result.Timesheet!.TimeId.Should().Be(4242);
        await api.Received(1).CreateTimesheetAsync(
            Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_WhenTheApiReturnsAnId_ReadsThatRowBack()
    {
        var api = ApiWithRate();
        api.CreateTimesheetAsync(Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TimesheetResponse { Success = true, TimesheetId = 4242 });
        api.GetTimesheetsAsync(Emp, new DateOnly(2026, 3, 16), Arg.Any<CancellationToken>())
            .Returns([CreatedRow(4242)]);
        var service = new TimesheetCreateService(api, Config());
        var prepared = await service.PrepareAsync(Emp, Options(Description: "Something else"), Ct);

        var result = await service.ApplyAsync(prepared.Plan!, Ct);

        result.TimesheetId.Should().Be(4242);
        result.Timesheet!.TimeId.Should().Be(4242);
    }

    [Fact]
    public async Task Apply_WhenTheApiReportsFailure_ReturnsItWithoutReadingBack()
    {
        var api = ApiWithRate();
        api.CreateTimesheetAsync(Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TimesheetResponse { Success = false, Message = "Duplicate entry" });
        var service = new TimesheetCreateService(api, Config());
        var prepared = await service.PrepareAsync(Emp, Options(), Ct);

        var result = await service.ApplyAsync(prepared.Plan!, Ct);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Duplicate entry");
        result.Timesheet.Should().BeNull();
        await api.DidNotReceive().GetTimesheetsAsync(
            Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    private static TimesheetCreateOptions Options(
        string? Location = null,
        string? Category = null,
        string? Billable = null,
        string? Description = null,
        int? Less = null,
        decimal? SellPrice = null) =>
        new(Client, Project, Date, Description: Description, Location: Location, Category: Category,
            IterationId: 3402, Billable: Billable, Less: Less, SellPrice: SellPrice);

    private static ITimeProApiClient ApiWithRate(string? expiry = "2026-12-31")
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetClientRateAsync(Emp, Client, new DateOnly(2026, 3, 16), Arg.Any<CancellationToken>())
            .Returns(new ClientRateResponse
            {
                EmpId = Emp,
                ClientId = Client,
                Rate = 175m,
                PrepaidRate = 150m,
                ExpiryDate = expiry
            });
        api.QueryTimesheetsAsync(Arg.Any<TimesheetSummaryFilter>(), Arg.Any<CancellationToken>())
            .Returns([]);
        return api;
    }

    private static IConfigService Config(string? mappedCategory = null)
    {
        var config = Substitute.For<IConfigService>();
        config.LoadGlobalConfig().Returns(new GlobalConfig { DefaultLocation = "Office", WfhDays = [] });
        config.LoadRepoMappings().Returns(mappedCategory is null
            ? []
            : [
                new RepoMappingEntry
                {
                    PathPattern = "~/code/traders-app",
                    ClientId = Client,
                    ProjectId = Project,
                    CategoryId = mappedCategory
                }
            ]);
        return config;
    }

    private static TimesheetItem CreatedRow(int timeId) => new()
    {
        TimeId = timeId,
        EmpId = Emp,
        ClientId = Client,
        ProjectId = Project,
        Notes = "Product search",
        Date = "2026-03-16T00:00:00",
        StartTime = "2026-03-16T09:00:00",
        EndTime = "2026-03-16T17:00:00",
        BillableId = "B",
        IsSuggested = false
    };
}
