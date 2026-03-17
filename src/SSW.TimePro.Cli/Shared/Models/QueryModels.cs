using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Shared.Models;

public class TimesheetSummaryFilter
{
    [JsonPropertyName("EmployeeIds")]
    public List<string> EmployeeIds { get; set; } = [];

    [JsonPropertyName("StartDate")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("EndDate")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("CategoryIds")]
    public List<string> CategoryIds { get; set; } = [];

    [JsonPropertyName("ClientIds")]
    public List<string> ClientIds { get; set; } = [];

    [JsonPropertyName("ProjectIds")]
    public List<string> ProjectIds { get; set; } = [];
}

public class TimesheetSummaryEntry
{
    public int TimeId { get; set; }
    public string? TimesheetDate { get; set; }
    public string? EmpId { get; set; }
    public string? EmployeeName { get; set; }
    public string? BillableId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? Note { get; set; }
    public decimal TotalHours { get; set; }
    public decimal SellPrice { get; set; }
}
