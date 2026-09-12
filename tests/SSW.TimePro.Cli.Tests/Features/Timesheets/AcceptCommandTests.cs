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

public class AcceptCommandTests
{
    private static readonly DateOnly Day = new(2026, 3, 16);

    [Fact]
    public async Task Accept_WhenProjectUsesIterationsAndNoneGiven_FailsBeforeCallingTheApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureSuggestion(api);
        var app = CreateApp(api);

        var output = await UpdateCommandTests.CaptureStdoutAsync(() => app.RunAsync([
            "accept",
            "42",
            "--date", "2026-03-16",
            "--json"
        ], TestContext.Current.CancellationToken));

        output.ExitCode.Should().Be(1);
        await api.DidNotReceive().AcceptSuggestedTimesheetAsync(
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
        using var doc = JsonDocument.Parse(output.Stdout);
        doc.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("Checkout API (3402)");
    }

    [Fact]
    public async Task Accept_WhenIterationIsGiven_AppliesItToTheAcceptedEntry()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureSuggestion(api);
        api.AcceptSuggestedTimesheetAsync(42, null, null, null, Arg.Any<CancellationToken>())
            .Returns(new TimesheetResponse { Success = true, TimesheetId = 77 })
            .AndDoes(_ => ConfigureDay(api, Accepted()));
        TimesheetRequest? request = null;
        api.UpdateTimesheetAsync(
                Arg.Do<TimesheetRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new TimesheetResponse { Success = true });
        var app = CreateApp(api);

        var output = await UpdateCommandTests.CaptureStdoutAsync(() => app.RunAsync([
            "accept",
            "42",
            "--date", "2026-03-16",
            "--iteration", "Checkout API",
            "--json"
        ], TestContext.Current.CancellationToken));

        output.ExitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.TimeId.Should().Be(77);
        request.IterationId.Should().Be(3402);

        using var doc = JsonDocument.Parse(output.Stdout);
        doc.RootElement.GetProperty("timesheetId").GetInt32().Should().Be(77);
        doc.RootElement.GetProperty("timesheet").GetProperty("timeId").GetInt32().Should().Be(77);
    }

    [Fact]
    public async Task Accept_WhenApiReturnsEmptyBody_ReadsTheAcceptedEntryBack()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureSuggestion(api, projectUsesIterations: false);
        api.AcceptSuggestedTimesheetAsync(42, null, null, null, Arg.Any<CancellationToken>())
            .Returns((TimesheetResponse?)null)
            .AndDoes(_ => ConfigureDay(api, Accepted()));
        var app = CreateApp(api);

        var output = await UpdateCommandTests.CaptureStdoutAsync(() => app.RunAsync([
            "accept",
            "42",
            "--date", "2026-03-16",
            "--json"
        ], TestContext.Current.CancellationToken));

        output.ExitCode.Should().Be(0);
        output.Stdout.Trim().Should().NotBe("null");
        using var doc = JsonDocument.Parse(output.Stdout);
        doc.RootElement.GetProperty("timesheetId").GetInt32().Should().Be(77);
        doc.RootElement.GetProperty("timesheet").GetProperty("notes").GetString().Should().Be("Product search");
    }

    [Fact]
    public async Task Accept_WhenAnotherRowAppearsAlongsideTheAcceptedOne_PicksTheMatchingRow()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureSuggestion(api);
        api.AcceptSuggestedTimesheetAsync(42, null, null, null, Arg.Any<CancellationToken>())
            .Returns((TimesheetResponse?)null)
            .AndDoes(_ => ConfigureDay(api, Accepted(), UnrelatedRow()));
        TimesheetRequest? request = null;
        api.UpdateTimesheetAsync(
                Arg.Do<TimesheetRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new TimesheetResponse { Success = true });
        var app = CreateApp(api);

        var output = await UpdateCommandTests.CaptureStdoutAsync(() => app.RunAsync([
            "accept",
            "42",
            "--date", "2026-03-16",
            "--iteration", "Checkout API",
            "--json"
        ], TestContext.Current.CancellationToken));

        output.ExitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.TimeId.Should().Be(77, because: "the unrelated row must never be updated");
        using var doc = JsonDocument.Parse(output.Stdout);
        doc.RootElement.GetProperty("iterationApplied").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Accept_WhenNoNewRowMatchesTheSuggestion_ReportsThatTheIterationWasNotApplied()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureSuggestion(api);
        api.AcceptSuggestedTimesheetAsync(42, null, null, null, Arg.Any<CancellationToken>())
            .Returns((TimesheetResponse?)null)
            .AndDoes(_ => ConfigureDay(api, UnrelatedRow()));
        var app = CreateApp(api);

        var output = await UpdateCommandTests.CaptureStdoutAsync(() => app.RunAsync([
            "accept",
            "42",
            "--date", "2026-03-16",
            "--iteration", "Checkout API",
            "--json"
        ], TestContext.Current.CancellationToken));

        output.ExitCode.Should().Be(1);
        await api.DidNotReceive().UpdateTimesheetAsync(
            Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>());

        using var doc = JsonDocument.Parse(output.Stdout);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("iterationApplied").GetBoolean().Should().BeFalse();
        var warning = doc.RootElement.GetProperty("warning").GetString()!;
        warning.Should().Contain("tp ts update");
        warning.Should().Contain("Do not accept again");
        warning.Should().NotContain("tp ts accept");
    }

    [Fact]
    public async Task Accept_WhenTwoNewRowsBothMatch_DoesNotGuessWhichOneToUpdate()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureSuggestion(api);
        var twin = Accepted();
        twin.TimeId = 78;
        api.AcceptSuggestedTimesheetAsync(42, null, null, null, Arg.Any<CancellationToken>())
            .Returns((TimesheetResponse?)null)
            .AndDoes(_ => ConfigureDay(api, Accepted(), twin));
        var app = CreateApp(api);

        var output = await UpdateCommandTests.CaptureStdoutAsync(() => app.RunAsync([
            "accept",
            "42",
            "--date", "2026-03-16",
            "--iteration", "Checkout API",
            "--json"
        ], TestContext.Current.CancellationToken));

        output.ExitCode.Should().Be(1);
        await api.DidNotReceive().UpdateTimesheetAsync(
            Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>());
        using var doc = JsonDocument.Parse(output.Stdout);
        doc.RootElement.GetProperty("iterationApplied").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Accept_WhenSuggestionAlreadyHasAnIterationAndNoneRequested_SkipsTheIterationLookup()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var suggestion = Suggestion();
        suggestion.Iteration = "Checkout API";
        suggestion.IterationId = 3402;
        ConfigureDay(api, suggestion);
        api.AcceptSuggestedTimesheetAsync(42, null, null, null, Arg.Any<CancellationToken>())
            .Returns(new TimesheetResponse { Success = true, TimesheetId = 77 })
            .AndDoes(_ => ConfigureDay(api, Accepted()));
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "accept",
            "42",
            "--date", "2026-03-16",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        await api.DidNotReceive().GetIterationsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static TimesheetItem UnrelatedRow() => new()
    {
        TimeId = 99,
        EmpId = "TST",
        ClientId = "NWIND",
        ProjectId = "4HCG7J",
        LocationId = "SSW",
        Notes = "Order history",
        Date = "2026-03-16T00:00:00",
        StartTime = "2026-03-16T13:00:00",
        EndTime = "2026-03-16T17:00:00",
        BillableId = "B"
    };

    private static TimesheetItem Suggestion() => new()
    {
        TimeId = 42,
        EmpId = "TST",
        ClientId = "NWIND",
        ProjectId = "1I776Q",
        LocationId = "Home",
        Notes = "Product search",
        Date = "2026-03-16T00:00:00",
        StartTime = "2026-03-16T09:00:00",
        EndTime = "2026-03-16T17:00:00",
        BillableId = "B",
        IsSuggested = true
    };

    private static TimesheetItem Accepted() => new()
    {
        TimeId = 77,
        EmpId = "TST",
        ClientId = "NWIND",
        ProjectId = "1I776Q",
        LocationId = "Home",
        Notes = "Product search",
        Date = "2026-03-16T00:00:00",
        StartTime = "2026-03-16T09:00:00",
        EndTime = "2026-03-16T17:00:00",
        BillableId = "B"
    };

    private static void ConfigureSuggestion(ITimeProApiClient api, bool projectUsesIterations = true)
    {
        ConfigureDay(api, Suggestion());
        api.GetIterationsAsync("1I776Q", Arg.Any<CancellationToken>())
            .Returns(projectUsesIterations
                ? [new IterationItem { IterationId = 3402, IterationName = "Checkout API" }]
                : []);
        api.QueryTimesheetsAsync(Arg.Any<TimesheetSummaryFilter>(), Arg.Any<CancellationToken>())
            .Returns([new TimesheetSummaryEntry { TimeId = 77, CategoryId = "WEBDEV", SellPrice = 175m }]);
    }

    private static void ConfigureDay(ITimeProApiClient api, params TimesheetItem[] entries)
    {
        api.GetTimesheetsAsync("TST", Day, Arg.Any<CancellationToken>())
            .Returns(entries.ToList());
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
        services.AddSingleton<TimesheetUpdateService>();
        services.AddSingleton<TimesheetAcceptService>();

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator => configurator.AddCommand<AcceptCommand>("accept"));
        return app;
    }
}
