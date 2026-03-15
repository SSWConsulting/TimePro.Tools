namespace SSW.TimePro.Cli.Shared.Models;

public class TimesheetLocation
{
    public string? LocationId { get; set; }
    public string? LocationName { get; set; }
}

public class TimesheetCategory
{
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public bool IsNonWorking { get; set; }
}

public class TimesheetBillableType
{
    public string? Value { get; set; }
    public string? Text { get; set; }
}
