namespace SSW.TimePro.Cli.Features.Leave;

/// <summary>A null LastStatus means the request was never found while polling.</summary>
public sealed record LeaveCancelWaitResult(bool Cancelled, string? LastStatus);

/// <summary>
/// Polls a leave request until the server finishes the cancellation it accepted asynchronously.
/// </summary>
public static class LeaveCancelWaiter
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    public static async Task<LeaveCancelWaitResult> WaitForCancelledAsync(
        LeaveLookup lookup,
        string leaveId,
        string? employeeId,
        TimeSpan timeout,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken ct = default)
    {
        var clock = timeProvider ?? TimeProvider.System;
        Func<TimeSpan, CancellationToken, Task> wait =
            (duration, token) => Task.Delay(duration, clock, token);
        if (delay is not null)
            wait = delay;

        var deadline = clock.GetUtcNow() + timeout;
        string? lastStatus = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var remaining = deadline - clock.GetUtcNow();

            // A lookup pages the whole leave list, so cap it to the wait that is left. The poll that
            // lands on the deadline still gets one interval, so a wait never ends without a final read.
            var budget = remaining > TimeSpan.Zero ? remaining : PollInterval;

            try
            {
                using var deadlineCts = new CancellationTokenSource(budget, clock);
                using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct, deadlineCts.Token);

                var entry = await lookup.FindAsync(leaveId, employeeId, pollCts.Token);
                if (entry is not null)
                {
                    lastStatus = entry.StatusName;
                    if (entry.LeaveStatus == LeaveStatusRules.Cancelled)
                        return new LeaveCancelWaitResult(true, lastStatus);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new LeaveCancelWaitResult(false, lastStatus);
            }

            remaining = deadline - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return new LeaveCancelWaitResult(false, lastStatus);

            await wait(remaining < PollInterval ? remaining : PollInterval, ct);
        }
    }
}
