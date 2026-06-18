using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Shared.Models;

public class LeaveListResponse
{
    public PaginatedList<LeaveEntry>? Leaves { get; set; }

    /// <summary>Number of cancelled leave entries in this result set (list envelope field "cancelledCount").</summary>
    public int CancelledCount { get; set; }
}

public class PaginatedList<T>
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public List<T> Items { get; set; } = [];
}

public class LeaveEntry
{
    public string Id { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? CreatedAt { get; set; }

    /// <summary>Last-modified timestamp from the server (API field "updatedAt").</summary>
    public string? UpdatedAt { get; set; }

    public string? Note { get; set; }
    public string? ApprovedBy { get; set; }
    public string? RequestedEmpId { get; set; }

    /// <summary>Number of working days this leave spans (API field "daysAway").</summary>
    public decimal DaysAway { get; set; }

    /// <summary>Manual hours override applied to the leave, if any (API field "timeLessOverride").</summary>
    public decimal? TimeLessOverride { get; set; }

    /// <summary>Reason supplied when the leave was cancelled (API field "cancellationReason").</summary>
    public string? CancellationReason { get; set; }

    /// <summary>Local start without TZ offset (e.g. "2026-03-30T00:00:00"). Prefer the date portion for day matching.</summary>
    [JsonPropertyName("startDateWithoutOffset")]
    public string? StartDateLocal { get; set; }

    [JsonPropertyName("endDateWithoutOffset")]
    public string? EndDateLocal { get; set; }

    /// <summary>True = full-day (or multi-day) leave covering the whole day(s). False = a time-range / partial-day
    /// leave covering only <see cref="Length"/> hours (the rest of the day still needs a timesheet).</summary>
    public bool AllDay { get; set; }

    /// <summary>Leave duration in hours (API field "length"). Replaces the never-populated "totalHours".</summary>
    public decimal Length { get; set; }

    public LeaveTypeInfo? LeaveType { get; set; }

    /// <summary>The API field is "status", NOT "leaveStatus" — without this binding it defaults to 0 → "Unknown".</summary>
    [JsonPropertyName("status")]
    public int LeaveStatus { get; set; }

    public string StatusName => LeaveStatus switch
    {
        1 => "Pending",
        2 => "Approved",
        3 => "Synced",
        4 => "Processed",
        5 => "Declined",
        6 => "Error",
        7 => "Cancelled",
        8 => "PendingCancellation",
        9 => "PendingApproval",
        _ => "Unknown"
    };
}

/// <summary>
/// Per-employee leave summary from <c>GET /api/leave/stats/{employeeId}</c>.
/// Note: TimePro does not expose entitlement/remaining per leave type — this is
/// the only per-person leave-balance signal the API provides.
/// </summary>
public class LeaveStats
{
    /// <summary>Days since the employee's most recent leave. Null if no prior leave on record.</summary>
    public int? DaysSinceLastLeave { get; set; }

    /// <summary>Total leave (hours) taken by the employee in the last 12 months.</summary>
    public decimal LeaveTakenInLast12Months { get; set; }
}

public class LeaveTypeInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class CreateLeaveRequest
{
    public string RequestedEmpId { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int LeaveTypeId { get; set; }
    public string? Note { get; set; }
    public string UserStartTime { get; set; } = "09:00:00";
    public string UserEndTime { get; set; } = "18:00:00";
    public bool AllDay { get; set; } = true;
    public List<string> OptionalEmp { get; set; } = [];
    public string? ApprovedBy { get; set; }
    public decimal? TimeLessOverride { get; set; }
}

public class UpdateLeaveRequest
{
    public string Id { get; set; } = string.Empty;
    public string RequestedEmpId { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int LeaveTypeId { get; set; }
    public string? Note { get; set; }
    public string UserStartTime { get; set; } = "09:00:00";
    public string UserEndTime { get; set; } = "18:00:00";
    public bool AllDay { get; set; } = true;
    public List<string> OptionalEmp { get; set; } = [];
    public string? ApprovedBy { get; set; }
    public decimal? TimeLessOverride { get; set; }
}

public class CancelLeaveRequest
{
    public string LeaveId { get; set; } = string.Empty;
    public string CancellationReason { get; set; } = string.Empty;
}
