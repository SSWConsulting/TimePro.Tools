namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Response item from GET /api/Timesheets/GetTimesheetListViewModel.
/// </summary>
public class TimesheetItem
{
    public int TimeId { get; set; }
    public string EmpId { get; set; } = string.Empty;
    public string? EmpName { get; set; }
    public string? Client { get; set; }
    public string? ClientId { get; set; }
    public string? Project { get; set; }
    public string? ProjectId { get; set; }
    public string? Iteration { get; set; }
    public string? Category { get; set; }
    public string? Location { get; set; }
    public string? LocationId { get; set; }
    public string? Notes { get; set; }
    public string? Date { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? BillableId { get; set; }
    public bool IsBillable { get; set; }
    public decimal Less { get; set; }
    public decimal TotalTime { get; set; }
    public bool HasNotes { get; set; }
    public bool IsSuggested { get; set; }
    public bool IsLeave { get; set; }
    public string? InputSource { get; set; }

    // Invoice info (may be null)
    public int? InvoiceId { get; set; }
    public string? InvoiceType { get; set; }
    public bool IsLocked { get; set; }
}

/// <summary>
/// Request body for POST /api/Timesheets/SaveTimesheet (create and update).
/// </summary>
public class TimesheetRequest
{
    public string EmpId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int? IterationId { get; set; }
    public string? CategoryId { get; set; }
    public string? LocationId { get; set; }
    public string? DateCreated { get; set; }
    public string? TimeStart { get; set; }
    public string? TimeEnd { get; set; }
    public decimal? TimeLess { get; set; }
    public string? Note { get; set; }
    public string? BillableId { get; set; }
    public decimal? SellPrice { get; set; }
    public bool IsOverridden { get; set; }
    public bool IsOverwriteRate { get; set; }

    /// <summary>
    /// Only set for update operations.
    /// </summary>
    public int? TimeId { get; set; }
}

/// <summary>
/// Response from POST /api/Timesheets/SaveTimesheet.
/// </summary>
public class TimesheetResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? TimesheetId { get; set; }
}
