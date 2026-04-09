namespace SSW.TimePro.Cli.Shared.Models;

public class LeaveListResponse
{
    public PaginatedList<LeaveEntry>? Leaves { get; set; }
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
    public string? Note { get; set; }
    public string? ApprovedBy { get; set; }
    public string? RequestedEmpId { get; set; }
    public decimal TotalHours { get; set; }
    public LeaveTypeInfo? LeaveType { get; set; }
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
