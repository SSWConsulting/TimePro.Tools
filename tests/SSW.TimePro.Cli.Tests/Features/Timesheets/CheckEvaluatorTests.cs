using FluentAssertions;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Timesheets;

public class CheckEvaluatorTests
{
    private static readonly DateOnly Monday = new(2026, 3, 30); // arbitrary weekday

    private static TimesheetItem Ts(int id, decimal hours, string? start = null, string? end = null, string? notes = "work")
        => new()
        {
            TimeId = id,
            TotalTime = hours,
            StartTime = start,
            EndTime = end,
            Notes = notes,
            HasNotes = !string.IsNullOrWhiteSpace(notes),
            IsSuggested = false
        };

    private static LeaveEntry Leave(
        string id, string start, string end, bool allDay, decimal length,
        int status = 2, string typeName = "Annual Leave")
        => new()
        {
            Id = id,
            StartDateLocal = start,
            EndDateLocal = end,
            AllDay = allDay,
            Length = length,
            LeaveStatus = status,
            LeaveType = new LeaveTypeInfo { Id = 1, Name = typeName, IsActive = true }
        };

    // ── ToLeaveDays ──────────────────────────────────────────────

    [Fact]
    public void ToLeaveDays_KeepsOnlyApproved_AndDedupesById()
    {
        var entries = new[]
        {
            Leave("1", "2026-03-30T00:00:00", "2026-03-30T23:59:00", true, 8m, status: 2),
            Leave("1", "2026-03-30T00:00:00", "2026-03-30T23:59:00", true, 8m, status: 2), // dup id
            Leave("2", "2026-03-31T00:00:00", "2026-03-31T23:59:00", true, 8m, status: 7), // cancelled
            Leave("3", "2026-04-01T00:00:00", "2026-04-01T23:59:00", true, 8m, status: 1), // pending
        };

        var days = CheckEvaluator.ToLeaveDays(entries);

        days.Should().HaveCount(1);
        days[0].Start.Should().Be(new DateOnly(2026, 3, 30));
        days[0].AllDay.Should().BeTrue();
    }

    [Fact]
    public void ToLeaveDays_ParsesDateOnly_IgnoringTimeAndOffset()
    {
        var entries = new[] { Leave("1", "2026-03-30T00:00:00", "2026-04-03T23:59:00", true, 40m) };

        var days = CheckEvaluator.ToLeaveDays(entries);

        days.Single().Start.Should().Be(new DateOnly(2026, 3, 30));
        days.Single().End.Should().Be(new DateOnly(2026, 4, 3));
        days.Single().Covers(new DateOnly(2026, 4, 1)).Should().BeTrue();
    }

    [Fact]
    public void ToLeaveDays_FallsBackToStartEndDate_WhenLocalMissing()
    {
        var e = new LeaveEntry
        {
            Id = "1",
            StartDate = "2026-03-30T00:00:00+10:00",
            EndDate = "2026-03-30T23:59:00+10:00",
            AllDay = true,
            Length = 8m,
            LeaveStatus = 2,
            LeaveType = new LeaveTypeInfo { Name = "Annual Leave" }
        };

        var days = CheckEvaluator.ToLeaveDays(new[] { e });

        days.Single().Start.Should().Be(new DateOnly(2026, 3, 30));
    }

    // ── EvaluateDay ──────────────────────────────────────────────

    [Fact]
    public void EvaluateDay_NoTimesheetsNoLeave_IsErrorAndMissing()
    {
        var check = CheckEvaluator.EvaluateDay(Monday, [], 0, []);

        check.Covered.Should().BeFalse();
        check.CoverReason.Should().Be("missing");
        check.LeaveHours.Should().Be(0m);
        check.Issues.Should().ContainSingle(i => i.Severity == "error" && i.Message == "No timesheets entered");
    }

    [Fact]
    public void EvaluateDay_FullDayApprovedLeave_NoTimesheet_IsCoveredLeaveFull_NoError()
    {
        var leave = CheckEvaluator.ToLeaveDays(new[]
        {
            Leave("1", "2026-03-30T00:00:00", "2026-03-30T23:59:00", true, 8m, typeName: "Annual Leave")
        });

        var check = CheckEvaluator.EvaluateDay(Monday, [], 0, leave);

        check.Covered.Should().BeTrue();
        check.CoverReason.Should().Be("leave-full");
        check.LeaveHours.Should().Be(8m);
        check.LeaveType.Should().Be("Annual Leave");
        check.HasError.Should().BeFalse();
        check.Issues.Should().Contain(i => i.Severity == "info" && i.Message == "On leave (Annual Leave)");
    }

    [Fact]
    public void EvaluateDay_PublicHoliday_IsCoveredHoliday()
    {
        var leave = CheckEvaluator.ToLeaveDays(new[]
        {
            Leave("1", "2026-03-30T00:00:00", "2026-03-30T23:59:00", true, 8m, typeName: "Public Holiday")
        });

        var check = CheckEvaluator.EvaluateDay(Monday, [], 0, leave);

        check.Covered.Should().BeTrue();
        check.CoverReason.Should().Be("holiday");
        check.LeaveType.Should().Be("Public Holiday");
        check.HasError.Should().BeFalse();
        check.Issues.Should().Contain(i => i.Severity == "info" && i.Message == "Public Holiday");
    }

    [Fact]
    public void EvaluateDay_PartialLeavePlusEnoughLogged_IsCovered()
    {
        // 2.5h leave + 5.5h logged = fully covered.
        var leave = CheckEvaluator.ToLeaveDays(new[]
        {
            Leave("1", "2026-03-30T00:00:00", "2026-03-30T23:59:00", allDay: false, length: 2.5m)
        });

        var check = CheckEvaluator.EvaluateDay(Monday, [Ts(1, 5.5m)], 0, leave);

        check.LeaveHours.Should().Be(2.5m);
        check.Covered.Should().BeTrue();
        check.CoverReason.Should().Be("leave-partial");
        check.HasError.Should().BeFalse();
        check.Issues.Should().NotContain(i => i.Severity == "warning");
    }

    [Fact]
    public void EvaluateDay_PartialLeaveInsufficientLogged_WarnsButNoError()
    {
        // 2h leave + 3h logged = under remaining 6h.
        var leave = CheckEvaluator.ToLeaveDays(new[]
        {
            Leave("1", "2026-03-30T00:00:00", "2026-03-30T23:59:00", allDay: false, length: 2m)
        });

        var check = CheckEvaluator.EvaluateDay(Monday, [Ts(1, 3m)], 0, leave);

        check.Covered.Should().BeFalse();
        check.CoverReason.Should().Be("leave-partial");
        check.HasError.Should().BeFalse();
        check.Issues.Should().Contain(i => i.Severity == "warning");
    }

    [Fact]
    public void EvaluateDay_CancelledLeave_NotCounted()
    {
        var leave = CheckEvaluator.ToLeaveDays(new[]
        {
            Leave("1", "2026-03-30T00:00:00", "2026-03-30T23:59:00", true, 8m, status: 7) // Cancelled
        });

        var check = CheckEvaluator.EvaluateDay(Monday, [], 0, leave);

        check.LeaveHours.Should().Be(0m);
        check.Covered.Should().BeFalse();
        check.CoverReason.Should().Be("missing");
        check.Issues.Should().Contain(i => i.Severity == "error");
    }

    [Fact]
    public void EvaluateDay_FullLoggedNoLeave_IsCoveredLogged()
    {
        var check = CheckEvaluator.EvaluateDay(Monday, [Ts(1, 8m)], 0, []);

        check.Covered.Should().BeTrue();
        check.CoverReason.Should().Be("logged");
        check.LeaveHours.Should().Be(0m);
        check.HasError.Should().BeFalse();
    }

    [Fact]
    public void EvaluateDay_StillFlagsOverlapAndMissingDescriptionAndOver10()
    {
        var sheets = new[]
        {
            Ts(1, 6m, "09:00", "15:00", notes: null),  // missing notes
            Ts(2, 6m, "14:00", "20:00", notes: "x"),    // overlaps #1, pushes total to 12h
        };

        var check = CheckEvaluator.EvaluateDay(Monday, sheets, 0, []);

        check.Issues.Should().Contain(i => i.Severity == "error" && i.Message.Contains("Overlap"));
        check.Issues.Should().Contain(i => i.Severity == "warning" && i.Message.Contains("no description"));
        check.Issues.Should().Contain(i => i.Severity == "warning" && i.Message.Contains("Over 10"));
    }

    [Fact]
    public void EvaluateDay_SuggestedTimesheets_AddInfoEvenWhenCovered()
    {
        var check = CheckEvaluator.EvaluateDay(Monday, [Ts(1, 8m)], suggestedCount: 2, []);

        check.Issues.Should().Contain(i => i.Severity == "info" && i.Message.Contains("2 suggested"));
    }
}
