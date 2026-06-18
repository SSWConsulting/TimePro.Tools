using FluentAssertions;
using SSW.TimePro.Cli.Shared.Models;
using System.Text.Json;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Shared.Models;

public class LeaveModelTests
{
    // Mirrors the API client's read options (CamelCase + case-insensitive).
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Realistic /api/leave/ payload shape (placeholder data). The API field is "status"
    // (not "leaveStatus"), and carries "allDay" / "length" / "*WithoutOffset".
    private const string LeaveJson = """
    {
      "leaves": {
        "pageNumber": 1, "pageSize": 10, "totalItems": 2, "totalPages": 1,
        "items": [
          {
            "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
            "startDate": "2026-03-30T00:00:00+10:00",
            "endDate": "2026-03-30T23:59:00+10:00",
            "startDateWithoutOffset": "2026-03-30T00:00:00",
            "endDateWithoutOffset": "2026-03-30T23:59:00",
            "requestedEmpId": "TST",
            "leaveType": { "id": 1, "name": "Annual Leave", "isActive": true },
            "status": 2,
            "length": 8.0,
            "allDay": true
          },
          {
            "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
            "startDateWithoutOffset": "2026-04-01T15:00:00",
            "endDateWithoutOffset": "2026-04-01T18:00:00",
            "requestedEmpId": "TST",
            "leaveType": { "id": 1, "name": "Annual Leave", "isActive": true },
            "status": 7,
            "length": 2.5,
            "allDay": false
          }
        ]
      },
      "cancelledCount": 0
    }
    """;

    [Fact]
    public void Status_field_binds_and_maps_to_name()
    {
        var resp = JsonSerializer.Deserialize<LeaveListResponse>(LeaveJson, Opts);
        var items = resp!.Leaves!.Items;

        // Regression: the API sends "status", not "leaveStatus". Without the
        // [JsonPropertyName("status")] binding this stayed 0 → "Unknown".
        items[0].LeaveStatus.Should().Be(2);
        items[0].StatusName.Should().Be("Approved");
        items[0].StatusName.Should().NotBe("Unknown");

        items[1].LeaveStatus.Should().Be(7);
        items[1].StatusName.Should().Be("Cancelled");
    }

    [Fact]
    public void LeaveStats_deserializes_from_stats_endpoint_shape()
    {
        // Mirrors the live /api/leave/stats/{empId} payload.
        const string json = """{"daysSinceLastLeave":14,"leaveTakenInLast12Months":18.38}""";

        var stats = JsonSerializer.Deserialize<LeaveStats>(json, Opts);

        stats.Should().NotBeNull();
        stats!.DaysSinceLastLeave.Should().Be(14);
        stats.LeaveTakenInLast12Months.Should().Be(18.38m);
    }

    [Fact]
    public void LeaveStats_allows_null_daysSinceLastLeave()
    {
        const string json = """{"daysSinceLastLeave":null,"leaveTakenInLast12Months":0}""";

        var stats = JsonSerializer.Deserialize<LeaveStats>(json, Opts);

        stats!.DaysSinceLastLeave.Should().BeNull();
        stats.LeaveTakenInLast12Months.Should().Be(0m);
    }

    [Fact]
    public void AllDay_length_and_offset_free_dates_bind()
    {
        var items = JsonSerializer.Deserialize<LeaveListResponse>(LeaveJson, Opts)!.Leaves!.Items;

        // Full-day vs time-range distinction comes from allDay (was dropped before).
        items[0].AllDay.Should().BeTrue();
        items[0].Length.Should().Be(8.0m);
        items[0].StartDateLocal.Should().Be("2026-03-30T00:00:00");

        // Partial-day leave: covers only `length` hours of the day.
        items[1].AllDay.Should().BeFalse();
        items[1].Length.Should().Be(2.5m);
        items[1].StartDateLocal.Should().Be("2026-04-01T15:00:00");
    }
}
