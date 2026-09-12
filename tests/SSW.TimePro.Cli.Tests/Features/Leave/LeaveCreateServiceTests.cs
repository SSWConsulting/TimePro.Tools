using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Leave;

public class LeaveCreateServiceTests
{
    private const string EmpId = "BOB";

    [Fact]
    public async Task Apply_WhenExactlyOneEntryMatches_ReturnsTheCreatedEntry()
    {
        var api = Substitute.For<ITimeProApiClient>();
        StubList(api, LeaveListService.Upcoming, Entry("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa"));
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.LeaveId.Should().Be("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa");
        result.Leave!.Note.Should().Be("Family day");
        result.Warning.Should().BeNull();
        await api.Received(1).CreateLeaveAsync(Arg.Any<CreateLeaveRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_WhenOnlyThePastFilterMatches_ReturnsTheCreatedEntry()
    {
        var api = Substitute.For<ITimeProApiClient>();
        StubList(api, LeaveListService.Upcoming);
        StubList(api, LeaveListService.Past, Entry("1a3c8f8b-2222-4444-8888-bbbbbbbbbbbb"));
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.LeaveId.Should().Be("1a3c8f8b-2222-4444-8888-bbbbbbbbbbbb");
    }

    [Fact]
    public async Task Apply_WhenNothingMatches_WarnsWithoutCreatingAgain()
    {
        var api = Substitute.For<ITimeProApiClient>();
        StubList(api, LeaveListService.Upcoming, Entry("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa", note: "Other leave"));
        StubList(api, LeaveListService.Past);
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.LeaveId.Should().BeNull();
        result.Leave.Should().BeNull();
        result.Warning.Should().Contain("no entry matches").And.Contain("tp leave list");
        await api.Received(1).CreateLeaveAsync(Arg.Any<CreateLeaveRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_WhenSeveralEntriesMatch_WarnsWithoutGuessing()
    {
        var api = Substitute.For<ITimeProApiClient>();
        StubList(
            api,
            LeaveListService.Upcoming,
            Entry("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa"),
            Entry("1a3c8f8b-2222-4444-8888-bbbbbbbbbbbb"));
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Leave.Should().BeNull();
        result.Warning.Should().Contain("2 entries match");
        await api.Received(1).CreateLeaveAsync(Arg.Any<CreateLeaveRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_WhenReadBackFails_StillReportsTheCreateAsSucceeded()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<LeaveListResponse?>>(_ => throw new ApiException(500, "boom"));
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Warning.Should().NotBeNull();
    }

    [Fact]
    public async Task Apply_WhenTheReadBackCannotConnect_StillReportsTheCreateAsSucceeded()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<LeaveListResponse?>>(_ => throw new TimeProConnectionException(
                "Connection refused", "tenant.json", "test", "https://timepro.example"));
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Leave.Should().BeNull();
        result.Warning.Should().Contain("Do not create it again");
        await api.Received(1).CreateLeaveAsync(Arg.Any<CreateLeaveRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_WhenTheReadBackThrowsAnythingElse_StillReportsTheCreateAsSucceeded()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<LeaveListResponse?>>(_ => throw new InvalidOperationException("unexpected"));
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Warning.Should().Contain("Do not create it again");
    }

    [Fact]
    public async Task Apply_PrefersTheOffsetFreeDates_WhenTheOffsetDatesFallOnAnotherDay()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var entry = Entry("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa");
        entry.StartDateLocal = "2026-04-01T00:00:00";
        entry.EndDateLocal = "2026-04-01T23:59:00";
        entry.StartDate = "2026-03-31T13:00:00+11:00";
        entry.EndDate = "2026-04-02T12:59:00+11:00";
        StubList(api, LeaveListService.Upcoming, entry);
        StubList(api, LeaveListService.Past);
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.LeaveId.Should().Be("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa");
    }

    [Fact]
    public async Task Apply_WhenTwoPartialDayRequestsShareTheDay_ResolvesTheSubmittedSlot()
    {
        var api = Substitute.For<ITimeProApiClient>();
        StubList(
            api,
            LeaveListService.Upcoming,
            PartialDayEntry("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa", "09:00", "13:00"),
            PartialDayEntry("1a3c8f8b-2222-4444-8888-bbbbbbbbbbbb", "13:00", "17:00"));
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service, halfDay: true, startTime: "13:00", endTime: "17:00"),
            TestContext.Current.CancellationToken);

        result.LeaveId.Should().Be("1a3c8f8b-2222-4444-8888-bbbbbbbbbbbb");
    }

    [Fact]
    public async Task Apply_WhenTwoPartialDayRequestsShareTheSlot_WarnsWithoutGuessing()
    {
        var api = Substitute.For<ITimeProApiClient>();
        StubList(
            api,
            LeaveListService.Upcoming,
            PartialDayEntry("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa", "13:00", "17:00"),
            PartialDayEntry("1a3c8f8b-2222-4444-8888-bbbbbbbbbbbb", "13:00", "17:00"));
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service, halfDay: true, startTime: "13:00", endTime: "17:00"),
            TestContext.Current.CancellationToken);

        result.Leave.Should().BeNull();
        result.Warning.Should().Contain("2 entries match");
    }

    [Fact]
    public async Task Apply_WhenOnlyAPartialDayEntryExists_DoesNotClaimItForAnAllDayRequest()
    {
        var api = Substitute.For<ITimeProApiClient>();
        StubList(api, LeaveListService.Upcoming, PartialDayEntry("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa", "09:00", "13:00"));
        StubList(api, LeaveListService.Past);
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service),
            TestContext.Current.CancellationToken);

        result.Leave.Should().BeNull();
        result.Warning.Should().Contain("no entry matches");
    }

    [Fact]
    public async Task Apply_WhenTheServerEchoesAnotherOffset_StillMatchesTheSlot()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var entry = PartialDayEntry("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa", "13:00", "17:00");
        entry.StartDateLocal = null;
        entry.EndDateLocal = null;
        entry.StartDate = "2026-04-01T23:00:00+10:00";
        entry.EndDate = "2026-04-02T03:00:00+10:00";
        StubList(api, LeaveListService.Upcoming, entry);
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        var result = await service.ApplyAsync(
            await PrepareAsync(service, halfDay: true, startTime: "13:00", endTime: "17:00"),
            TestContext.Current.CancellationToken);

        result.LeaveId.Should().Be("0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa");
    }

    [Fact]
    public async Task Prepare_DoesNotWrite()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var service = new LeaveCreateService(api, new LeaveLookup(api));

        await PrepareAsync(service);

        await api.DidNotReceive().CreateLeaveAsync(Arg.Any<CreateLeaveRequest>(), Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetLeaveAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static Task<LeaveCreatePlan> PrepareAsync(
        LeaveCreateService service,
        bool halfDay = false,
        string? startTime = null,
        string? endTime = null) =>
        service.PrepareAsync(
            EmpId,
            new LeaveCreateOptions(
                Start: "2026-04-01",
                End: "2026-04-01",
                Type: "1",
                Note: "Family day",
                HalfDay: halfDay,
                StartTime: startTime,
                EndTime: endTime,
                TimeZoneId: "UTC"),
            TestContext.Current.CancellationToken);

    private static LeaveEntry PartialDayEntry(string id, string startTime, string endTime)
    {
        var entry = Entry(id);
        entry.AllDay = false;
        entry.StartDateLocal = $"2026-04-01T{startTime}:00";
        entry.EndDateLocal = $"2026-04-01T{endTime}:00";
        entry.StartDate = $"2026-04-01T{startTime}:00+00:00";
        entry.EndDate = $"2026-04-01T{endTime}:00+00:00";
        return entry;
    }

    private static LeaveEntry Entry(string id, string note = "Family day") => new()
    {
        Id = id,
        StartDate = "2026-04-01T00:00:00+00:00",
        EndDate = "2026-04-01T23:59:00+00:00",
        Note = note,
        RequestedEmpId = EmpId,
        AllDay = true,
        LeaveStatus = 1,
        LeaveType = new LeaveTypeInfo { Id = 1, Name = "Annual Leave", IsActive = true }
    };

    private static void StubList(ITimeProApiClient api, string filter, params LeaveEntry[] items) =>
        api.GetLeaveAsync(filter, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new LeaveListResponse
            {
                Leaves = new PaginatedList<LeaveEntry>
                {
                    PageNumber = 1,
                    PageSize = 100,
                    TotalItems = items.Length,
                    TotalPages = 1,
                    Items = [.. items]
                }
            });
}
