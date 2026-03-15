namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Response from GET /api/Employees/GetEmployeeID.
/// </summary>
public class EmployeeIdResponse
{
    public string? EmpId { get; set; }
}

/// <summary>
/// Response from GET /api/v2/users/me.
/// </summary>
public class CurrentUserResponse
{
    public string? EmployeeId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? DefaultRate { get; set; }
}

/// <summary>
/// Response from GET /api/employees/getSettingsDetails.
/// </summary>
public class EmployeeSettings
{
    public string? EmployeeId { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? LunchBreakStart { get; set; }
    public string? LunchBreakEnd { get; set; }
    public int TimeLessMinutes { get; set; }
    public string? TimezoneId { get; set; }
}
