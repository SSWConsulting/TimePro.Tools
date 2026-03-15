namespace SSW.TimePro.Cli.Shared.Models;

public class ClientSearchResult
{
    public string? Value { get; set; }
    public string? Text { get; set; }
}

public class ProjectForSelect
{
    public string? Value { get; set; }
    public string? DisplayText { get; set; }
    public bool UseIteration { get; set; }
    public bool IsGeneral { get; set; }
    public bool IsLeave { get; set; }
}

public class ClientRateResponse
{
    public string? EmpId { get; set; }
    public string? ClientId { get; set; }
    public decimal? Rate { get; set; }
    public decimal? PrepaidRate { get; set; }
    public int? ClientRateId { get; set; }
    public string? EmployeeName { get; set; }
    public string? ClientName { get; set; }
    public string? ExpiryDate { get; set; }
    public string? Notes { get; set; }
}
