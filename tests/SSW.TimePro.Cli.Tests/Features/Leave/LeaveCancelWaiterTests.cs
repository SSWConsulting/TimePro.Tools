using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Leave;

public class LeaveCancelWaiterTests
{
    private const string LeaveId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    [Fact]
    public async Task WaitForCancelled_WhenStatusReachesCancelled_StopsWaiting()
    {
        var clock = new ManualClock();
        var api = ApiReturning([2, 8, 7]);

        var result = await LeaveCancelWaiter.WaitForCancelledAsync(
            new LeaveLookup(api),
            LeaveId,
            "BOB",
            TimeSpan.FromSeconds(120),
            clock,
            clock.Advance,
            TestContext.Current.CancellationToken);

        result.Cancelled.Should().BeTrue();
        result.LastStatus.Should().Be("Cancelled");
        clock.Elapsed.Should().Be(TimeSpan.FromSeconds(20));
        clock.Waits.Should().AllBeEquivalentTo(LeaveCancelWaiter.PollInterval);
    }

    [Fact]
    public async Task WaitForCancelled_WhenTimeoutPasses_StopsAtTheRequestedElapsedTime()
    {
        var clock = new ManualClock();
        var api = ApiReturning([8]);

        var result = await LeaveCancelWaiter.WaitForCancelledAsync(
            new LeaveLookup(api),
            LeaveId,
            "BOB",
            TimeSpan.FromSeconds(30),
            clock,
            clock.Advance,
            TestContext.Current.CancellationToken);

        result.Cancelled.Should().BeFalse();
        result.LastStatus.Should().Be("PendingCancellation");
        clock.Elapsed.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task WaitForCancelled_WhenTimeoutIsShorterThanThePollInterval_CapsTheWaitToTheTimeout()
    {
        var clock = new ManualClock();
        var api = ApiReturning([8]);

        var result = await LeaveCancelWaiter.WaitForCancelledAsync(
            new LeaveLookup(api),
            LeaveId,
            "BOB",
            TimeSpan.FromSeconds(5),
            clock,
            clock.Advance,
            TestContext.Current.CancellationToken);

        result.Cancelled.Should().BeFalse();
        clock.Elapsed.Should().Be(TimeSpan.FromSeconds(5));
        clock.Waits.Should().Equal(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForCancelled_WhenLookupOutlivesTheDeadline_CancelsTheLookup()
    {
        var clock = new ManualClock();
        var api = Substitute.For<ITimeProApiClient>();
        var polls = 0;
        var cancelledMidLookup = false;
        api.GetLeaveAsync(LeaveListService.Upcoming, 1, 100, "BOB", Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (polls++ > 0)
                {
                    clock.Advance(TimeSpan.FromSeconds(60));
                    var token = call.Arg<CancellationToken>();
                    cancelledMidLookup = token.IsCancellationRequested;
                    token.ThrowIfCancellationRequested();
                }

                return Page(8);
            });

        var result = await LeaveCancelWaiter.WaitForCancelledAsync(
            new LeaveLookup(api),
            LeaveId,
            "BOB",
            TimeSpan.FromSeconds(30),
            clock,
            clock.Advance,
            TestContext.Current.CancellationToken);

        cancelledMidLookup.Should().BeTrue("the lookup token must be capped to the remaining wait");
        result.Cancelled.Should().BeFalse();
        result.LastStatus.Should().Be("PendingCancellation");
    }

    [Fact]
    public async Task WaitForCancelled_WhenTheRequestIsNeverFound_ReportsNoStatus()
    {
        var clock = new ManualClock();
        var api = Substitute.For<ITimeProApiClient>();

        var result = await LeaveCancelWaiter.WaitForCancelledAsync(
            new LeaveLookup(api),
            LeaveId,
            "BOB",
            TimeSpan.FromSeconds(20),
            clock,
            clock.Advance,
            TestContext.Current.CancellationToken);

        result.Cancelled.Should().BeFalse();
        result.LastStatus.Should().BeNull();
        clock.Elapsed.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task WaitForCancelled_WhenCancelledDuringTheFinalInterval_ObservesTheCancellation()
    {
        var clock = new ManualClock();
        var api = ApiReturning([8]);
        using var cts = new CancellationTokenSource();

        var act = () => LeaveCancelWaiter.WaitForCancelledAsync(
            new LeaveLookup(api),
            LeaveId,
            "BOB",
            TimeSpan.FromSeconds(30),
            clock,
            (duration, token) =>
            {
                // Cancel inside the last interval before the deadline would be reached.
                if (clock.Elapsed >= TimeSpan.FromSeconds(20))
                    cts.Cancel();
                return clock.Advance(duration, token);
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    private static ITimeProApiClient ApiReturning(int[] statuses)
    {
        var index = 0;
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveAsync(LeaveListService.Upcoming, 1, 100, "BOB", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var status = statuses[Math.Min(index, statuses.Length - 1)];
                index++;
                return Page(status);
            });
        return api;
    }

    private static LeaveListResponse Page(int status) => new()
    {
        Leaves = new PaginatedList<LeaveEntry>
        {
            PageNumber = 1,
            PageSize = 100,
            TotalItems = 1,
            TotalPages = 1,
            Items = [new LeaveEntry { Id = LeaveId, RequestedEmpId = "BOB", LeaveStatus = status }]
        }
    };

    /// <summary>
    /// Time only moves when the waiter waits or a lookup says it took time, so tests assert on
    /// elapsed time. Timers are driven by the same clock, so the waiter's deadline really fires.
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 3, 30, 9, 0, 0, TimeSpan.Zero);

        public TimeSpan Elapsed { get; private set; }

        public List<TimeSpan> Waits { get; } = [];

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public Task Advance(TimeSpan duration, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Waits.Add(duration);
            Advance(duration);
            return Task.CompletedTask;
        }

        public void Advance(TimeSpan duration)
        {
            _now += duration;
            Elapsed += duration;

            foreach (var timer in _timers.ToArray())
                timer.FireIfDue(_now);
        }

        private void Remove(ManualTimer timer) => _timers.Remove(timer);

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _dueAt;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
                return true;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                if (_dueAt is null || _dueAt > now)
                    return;

                _dueAt = null;
                callback(state);
            }

            public void Dispose() => clock.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
