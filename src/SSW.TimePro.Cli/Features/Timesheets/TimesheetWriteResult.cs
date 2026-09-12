using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Timesheets;

/// <summary>
/// Outcome of a timesheet write, shared by the CLI <c>--json</c> path and the MCP tools.
/// <see cref="Timesheet"/> is the entry re-read after the save, because SaveTimesheet and
/// AcceptSuggestedTimesheet usually answer with an empty body.
/// </summary>
public sealed record TimesheetWriteResult
{
    public bool Success { get; init; } = true;
    public int? TimesheetId { get; init; }
    public string? Message { get; init; }
    public TimesheetItem? Timesheet { get; init; }
}
