using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Integration.Features;

/// <summary>
/// End-to-end leave-aware coverage: mocks the per-day timesheet endpoints AND the
/// leave UPCOMING/PAST endpoints, fetches via the real <see cref="ApiClient"/>, and
/// runs the data through <see cref="CheckEvaluator"/> exactly as the command does.
///
/// Week under test: Mon 2026-03-30 .. Fri 2026-04-03 (placeholder data only).
///   Mon 30 - full timesheet (8h), no leave        → covered, "logged"
///   Tue 31 - NO timesheet, full-day approved leave → covered, "leave-full" (NOT an error)
///   Wed 01 - 5.5h timesheet + 2.5h approved partial → covered, "leave-partial"
///   Thu 02 - NO timesheet, CANCELLED leave         → NOT covered, error "missing"
///   Fri 03 - full timesheet (8h), no leave         → covered, "logged"
/// </summary>
public class CheckLeaveTests : TestBase
{
    private static readonly DateOnly Monday = new(2026, 3, 30);

    private void StubDay(DateOnly date, string bodyJson)
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/Timesheets/GetTimesheetListViewModel")
                .WithParam("employeeID", "TST")
                .WithParam("date", date.ToString("yyyy-MM-dd"))
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(bodyJson)
        );
    }

    private void StubLeave(string filter)
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/")
                .WithParam("leaveFilter", filter)
                .WithParam("employeeId", "TST")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromFile("Fixtures/leave-check-week.json")
        );
    }

    private static string OneSheet(string date, decimal hours, string start, string end) => $$"""
    [
      {
        "timeId": 9001,
        "empId": "TST",
        "project": "Internal",
        "projectId": "1I776Q",
        "notes": "Product search work",
        "date": "{{date}}",
        "startTime": "{{start}}",
        "endTime": "{{end}}",
        "totalTime": {{hours}},
        "hasNotes": true,
        "isSuggested": false,
        "isLeave": false
      }
    ]
    """;

    private async Task<List<CheckEvaluator.DayCheck>> RunCheckAsync()
    {
        // Leave fetched once for the run (UPCOMING + PAST), deduped + approved-only.
        StubLeave("UPCOMING");
        StubLeave("PAST");

        var entries = new List<LeaveEntry>();
        foreach (var filter in new[] { "UPCOMING", "PAST" })
        {
            var resp = await ApiClient.GetLeaveAsync(filter, 1, 200, "TST", CancellationToken.None);
            if (resp?.Leaves?.Items is { } items) entries.AddRange(items);
        }
        var approved = CheckEvaluator.ToLeaveDays(entries);

        // Per-day timesheets.
        StubDay(Monday, OneSheet("2026-03-30", 8.0m, "09:00", "17:00"));      // Mon: full
        StubDay(Monday.AddDays(1), "[]");                                      // Tue: none (full leave)
        StubDay(Monday.AddDays(2), OneSheet("2026-04-01", 5.5m, "12:00", "17:30")); // Wed: partial
        StubDay(Monday.AddDays(3), "[]");                                      // Thu: none (cancelled leave)
        StubDay(Monday.AddDays(4), OneSheet("2026-04-03", 8.0m, "09:00", "17:00"));  // Fri: full

        var checks = new List<CheckEvaluator.DayCheck>();
        for (var d = Monday; d <= Monday.AddDays(4); d = d.AddDays(1))
        {
            var sheets = await ApiClient.GetTimesheetsAsync("TST", d, CancellationToken.None);
            var real = sheets.Where(t => !t.IsSuggested).ToList();
            var suggested = sheets.Count(t => t.IsSuggested);
            checks.Add(CheckEvaluator.EvaluateDay(d, real, suggested, approved));
        }
        return checks;
    }

    [Fact]
    public async Task FullDayApprovedLeave_NoTimesheet_IsCoveredNotError()
    {
        var checks = await RunCheckAsync();
        var tue = checks.Single(c => c.Date == new DateOnly(2026, 3, 31));

        tue.Covered.Should().BeTrue();
        tue.CoverReason.Should().Be("leave-full");
        tue.LeaveHours.Should().Be(8.0m);
        tue.LeaveType.Should().Be("Annual Leave");
        tue.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task PartialLeavePlusPartialTimesheet_IsCovered()
    {
        var checks = await RunCheckAsync();
        var wed = checks.Single(c => c.Date == new DateOnly(2026, 4, 1));

        wed.Covered.Should().BeTrue();
        wed.CoverReason.Should().Be("leave-partial");
        wed.LeaveHours.Should().Be(2.5m);
        wed.TotalHours.Should().Be(5.5m);
        wed.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task CancelledLeaveDay_NoTimesheet_IsNotCovered()
    {
        var checks = await RunCheckAsync();
        var thu = checks.Single(c => c.Date == new DateOnly(2026, 4, 2));

        thu.Covered.Should().BeFalse();
        thu.LeaveHours.Should().Be(0m);
        thu.CoverReason.Should().Be("missing");
        thu.HasError.Should().BeTrue();
    }

    [Fact]
    public async Task LoggedDays_AreCovered()
    {
        var checks = await RunCheckAsync();

        checks.Single(c => c.Date == Monday).CoverReason.Should().Be("logged");
        checks.Single(c => c.Date == Monday).Covered.Should().BeTrue();
        checks.Single(c => c.Date == new DateOnly(2026, 4, 3)).Covered.Should().BeTrue();
    }
}
