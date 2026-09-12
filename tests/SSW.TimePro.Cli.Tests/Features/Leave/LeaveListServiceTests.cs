using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Leave;

public class LeaveListServiceTests
{
    [Theory]
    [InlineData("upcoming", "UPCOMING")]
    [InlineData(" past ", "PAST")]
    [InlineData("All", "ALL")]
    [InlineData("", "UPCOMING")]
    [InlineData(null, "UPCOMING")]
    public void TryNormalizeFilter_AcceptsTheApiFiltersAndAll(string? input, string expected)
    {
        LeaveListService.TryNormalizeFilter(input, out var normalized, out var error).Should().BeTrue();
        normalized.Should().Be(expected);
        error.Should().BeNull();
    }

    [Fact]
    public void TryNormalizeFilter_WhenUnknown_ListsTheValidValues()
    {
        LeaveListService.TryNormalizeFilter("NOPE", out _, out var error).Should().BeFalse();
        error.Should().Contain("NOPE").And.Contain("UPCOMING, PAST, ALL");
    }

    [Fact]
    public async Task ListAsync_WhenFilterIsUnknown_DoesNotCallTheApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var service = new LeaveListService(api);

        var act = () => service.ListAsync("NOPE", 10, "BOB", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LeaveFilterValidationException>();
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetLeaveAsync));
    }

    [Fact]
    public async Task ListAsync_WhenFilterIsAll_MergesBothApiFilters()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveAsync(LeaveListService.Upcoming, 1, 10, "BOB", Arg.Any<CancellationToken>())
            .Returns(Response(cancelledCount: 3, ids: ["leave-1", "leave-2"]));
        api.GetLeaveAsync(LeaveListService.Past, 1, 10, "BOB", Arg.Any<CancellationToken>())
            .Returns(Response(cancelledCount: 3, ids: ["leave-3"]));
        var service = new LeaveListService(api);

        var result = await service.ListAsync("ALL", 10, "BOB", TestContext.Current.CancellationToken);

        result.Leaves!.Items.Select(entry => entry.Id).Should().Equal("leave-1", "leave-2", "leave-3");
        result.Leaves.TotalItems.Should().Be(3);
        result.Leaves.PageSize.Should().Be(10);
        result.CancelledCount.Should().Be(3);
    }

    [Fact]
    public async Task ListAsync_WhenFilterIsAll_DedupesEntriesReturnedByBothFilters()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveAsync(LeaveListService.Upcoming, 1, 10, null, Arg.Any<CancellationToken>())
            .Returns(Response(cancelledCount: 0, ids: ["leave-1", "leave-2"]));
        api.GetLeaveAsync(LeaveListService.Past, 1, 10, null, Arg.Any<CancellationToken>())
            .Returns(Response(cancelledCount: 0, ids: ["LEAVE-2", "leave-3"]));
        var service = new LeaveListService(api);

        var result = await service.ListAsync("ALL", 10, null, TestContext.Current.CancellationToken);

        result.Leaves!.Items.Select(entry => entry.Id).Should().Equal("leave-1", "leave-2", "leave-3");
    }

    [Fact]
    public async Task ListAsync_WhenFilterIsAll_NeverReturnsMoreThanTheLimit()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveAsync(LeaveListService.Upcoming, 1, 2, null, Arg.Any<CancellationToken>())
            .Returns(Response(cancelledCount: 0, ids: ["leave-1", "leave-2"]));
        api.GetLeaveAsync(LeaveListService.Past, 1, 2, null, Arg.Any<CancellationToken>())
            .Returns(Response(cancelledCount: 0, ids: ["leave-3", "leave-4"]));
        var service = new LeaveListService(api);

        var result = await service.ListAsync("ALL", 2, null, TestContext.Current.CancellationToken);

        result.Leaves!.Items.Select(entry => entry.Id).Should().Equal("leave-1", "leave-2");
    }

    [Fact]
    public async Task ListAsync_WhenFilterIsPast_PassesItStraightThrough()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveAsync(LeaveListService.Past, 1, 5, "BOB", Arg.Any<CancellationToken>())
            .Returns(Response(cancelledCount: 1, ids: ["leave-9"]));
        var service = new LeaveListService(api);

        var result = await service.ListAsync("past", 5, "BOB", TestContext.Current.CancellationToken);

        result.Leaves!.Items.Should().ContainSingle(entry => entry.Id == "leave-9");
        await api.DidNotReceive().GetLeaveAsync(
            LeaveListService.Upcoming,
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    private static LeaveListResponse Response(int cancelledCount, string[] ids) => new()
    {
        CancelledCount = cancelledCount,
        Leaves = new PaginatedList<LeaveEntry>
        {
            PageNumber = 1,
            PageSize = ids.Length,
            TotalItems = ids.Length,
            TotalPages = 1,
            Items = [.. ids.Select(id => new LeaveEntry { Id = id, RequestedEmpId = "BOB" })]
        }
    };
}
