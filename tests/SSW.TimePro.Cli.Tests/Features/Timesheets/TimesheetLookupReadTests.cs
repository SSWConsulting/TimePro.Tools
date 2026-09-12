using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Timesheets;

public class TimesheetLookupReadTests
{
    private const string Emp = "BOB";

    // Monday 16 March 2026 to Sunday 22 March 2026.
    private static readonly DateOnly Monday = new(2026, 3, 16);
    private static readonly DateOnly Saturday = new(2026, 3, 21);
    private static readonly DateOnly Sunday = new(2026, 3, 22);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ForRange_WithWeekendsIncluded_ReadsEveryDayInOrder()
    {
        var api = Api();

        var days = await TimesheetLookup.ForRangeAsync(api, Emp, Monday, Sunday, WeekendPolicy.Include, Ct);

        days.Select(d => d.Date).Should().Equal(Enumerable.Range(0, 7).Select(Monday.AddDays));
        await api.Received(7).GetTimesheetsAsync(Emp, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForRange_WithWeekendsSkipped_OmitsSaturdayAndSunday()
    {
        var api = Api();

        var days = await TimesheetLookup.ForRangeAsync(api, Emp, Monday, Sunday, WeekendPolicy.Skip, Ct);

        days.Select(d => d.Date).Should().Equal(Enumerable.Range(0, 5).Select(Monday.AddDays));
        await api.DidNotReceive().GetTimesheetsAsync(Emp, Saturday, Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetTimesheetsAsync(Emp, Sunday, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForRange_WithWeekendsSkipped_AndAWeekendOnlyRange_ReadsNothing()
    {
        var api = Api();

        var days = await TimesheetLookup.ForRangeAsync(api, Emp, Saturday, Saturday, WeekendPolicy.Skip, Ct);

        days.Should().BeEmpty();
        await api.DidNotReceive().GetTimesheetsAsync(
            Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForRange_WithWeekendsIncluded_AndAWeekendOnlyRange_ReadsTheDay()
    {
        var api = Api();

        var days = await TimesheetLookup.ForRangeAsync(api, Emp, Saturday, Saturday, WeekendPolicy.Include, Ct);

        days.Select(d => d.Date).Should().Equal([Saturday]);
        await api.Received(1).GetTimesheetsAsync(Emp, Saturday, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAndReadSuggested_RefreshesOnceThenReturnsOnlySuggestions()
    {
        var api = Api();
        api.GetTimesheetsAsync(Emp, Monday, Arg.Any<CancellationToken>()).Returns(
        [
            new TimesheetItem { TimeId = 1, IsSuggested = false },
            new TimesheetItem { TimeId = 2, IsSuggested = true }
        ]);

        var day = await TimesheetLookup.RefreshAndReadSuggestedAsync(api, Emp, Monday, Ct);

        day.Date.Should().Be(Monday);
        day.Entries.Select(t => t.TimeId).Should().Equal([2]);
        await api.Received(1).RefreshSuggestedTimesheetsAsync(Emp, Monday, Arg.Any<CancellationToken>());
        await api.Received(1).GetTimesheetsAsync(Emp, Monday, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAndReadSuggested_RefreshesBeforeReading()
    {
        var api = Api();

        await TimesheetLookup.RefreshAndReadSuggestedAsync(api, Emp, Monday, Ct);

        Received.InOrder(() =>
        {
            api.RefreshSuggestedTimesheetsAsync(Emp, Monday, Arg.Any<CancellationToken>());
            api.GetTimesheetsAsync(Emp, Monday, Arg.Any<CancellationToken>());
        });
    }

    private static ITimeProApiClient Api()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetTimesheetsAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
        return api;
    }
}
