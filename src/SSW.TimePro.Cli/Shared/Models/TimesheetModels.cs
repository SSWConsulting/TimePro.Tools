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
    public int? IterationId { get; set; }
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
    public int? InputSource { get; set; }

    // Invoice info (may be null)
    public int? InvoiceId { get; set; }
    public string? InvoiceType { get; set; }
    public bool IsLocked { get; set; }
}

/// <summary>
/// Request body for POST /api/Timesheets/SaveTimesheet (create and update).
/// Property names use [JsonPropertyName] to match the exact API contract.
/// </summary>
public class TimesheetRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("empID")]
    public string EmpId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("clientID")]
    public string ClientId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("projectID")]
    public string ProjectId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("iterationID")]
    public int? IterationId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("categoryID")]
    public string? CategoryId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("locationID")]
    public string? LocationId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("dateCreated")]
    public string? DateCreated { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timeStart")]
    public string? TimeStart { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timeEnd")]
    public string? TimeEnd { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timeLess")]
    public decimal? TimeLess { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("Notes")]
    public string? Note { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("billableID")]
    public string? BillableId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("sellPrice")]
    public decimal? SellPrice { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("IsBillingTypeOverridden")]
    public bool IsBillingTypeOverridden { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("isOverwriteRate")]
    public bool IsOverwriteRate { get; set; }

    // Server defaults a missing salesTaxPct to 0, producing $0 tax regardless of
    // billable type. The web UI sends this; the CLI must too. Leave null to let
    // the API client fill it from the client's configured tax rate.
    [System.Text.Json.Serialization.JsonPropertyName("salesTaxPct")]
    public decimal? SalesTaxPct { get; set; }

    /// <summary>
    /// Only set for update operations.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("timeID")]
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
