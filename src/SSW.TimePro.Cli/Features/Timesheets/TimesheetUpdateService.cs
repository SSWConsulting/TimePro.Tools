using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Timesheets;

public sealed record TimesheetUpdateOptions(
    string? Location = null,
    string? Description = null,
    string? Start = null,
    string? End = null,
    int? Less = null,
    string? ClientId = null,
    string? ProjectId = null,
    string? Category = null,
    string? Billable = null,
    decimal? SellPrice = null,
    string? Iteration = null,
    string? Date = null);

public sealed record TimesheetUpdatePlan(
    TimesheetItem Existing,
    TimesheetRequest Request,
    DateOnly Date,
    IReadOnlyList<string> Changes);

public class TimesheetValidationException(string message) : Exception(message);

public sealed class TimesheetNoChangesException(string message) : TimesheetValidationException(message);

/// <summary>
/// Builds a complete SaveTimesheet payload from the stored entry plus the caller's overrides.
/// SaveTimesheet?isEdit=true replaces the whole row, so anything the caller omits has to be read
/// back and re-sent or it is wiped.
/// </summary>
public sealed class TimesheetUpdateService
{
    private readonly ITimeProApiClient _api;

    public TimesheetUpdateService(ITimeProApiClient api) => _api = api;

    public async Task<TimesheetUpdatePlan> PrepareAsync(
        int timesheetId,
        string employeeId,
        TimesheetUpdateOptions options,
        CancellationToken ct = default)
    {
        if (options.Less is < 0)
            throw new TimesheetValidationException("Break/less time must be zero or greater");

        var found = await TimesheetLookup.FindAsync(_api, employeeId, timesheetId, options.Date, ct)
            ?? throw new TimesheetValidationException(
                $"Timesheet #{timesheetId} not found. Pass the date (yyyy-MM-dd) to narrow the search.");

        var existing = found.Item;
        if (existing.IsSuggested)
            throw new TimesheetValidationException(
                $"Timesheet {timesheetId} is a suggestion and cannot be updated. Accept it first: tp ts accept {timesheetId}");

        var changes = DescribeChanges(options);
        if (changes.Count == 0)
            throw new TimesheetNoChangesException(
                "No changes specified. Provide at least one field to change (location, description, iteration, times, less, client, project, category, billable, sell price).");

        var date = found.Day.Date;
        var dateText = date.ToString("yyyy-MM-dd");

        // The list GET returns neither CategoryID nor the sell-price snapshot — resolve both
        // from the query API entry for this timesheet.
        var existingEntry = await GetExistingEntryAsync(employeeId, date, timesheetId, ct);

        var targetProjectId = options.ProjectId ?? existing.ProjectId ?? "";
        var iterationId = await ResolveIterationAsync(existing, targetProjectId, options, ct);

        var request = new TimesheetRequest
        {
            TimeId = timesheetId,
            EmpId = employeeId,
            ClientId = options.ClientId ?? existing.ClientId ?? "",
            ProjectId = targetProjectId,
            IterationId = iterationId,
            DateCreated = dateText,
            TimeStart = options.Start is not null ? $"{dateText}T{options.Start}:00" : existing.StartTime,
            TimeEnd = options.End is not null ? $"{dateText}T{options.End}:00" : existing.EndTime,
            TimeLess = options.Less is not null
                ? options.Less.Value / 60m
                : existing.Less > 0 ? existing.Less : null,
            Note = options.Description ?? existing.Notes,
            LocationId = options.Location is not null
                ? LocationResolver.Resolve(options.Location)
                : existing.LocationId,
            CategoryId = options.Category ?? existingEntry?.CategoryId,
            BillableId = options.Billable ?? existing.BillableId ?? "B",

            // A timesheet's sell price is its own snapshot once created — independent of the client
            // rate — so a manager can e.g. discount one line without touching the rate or future
            // timesheets. SaveTimesheet requires one, so preserve it unless overridden.
            SellPrice = options.SellPrice ?? existingEntry?.SellPrice,
        };

        return new TimesheetUpdatePlan(existing, request, date, changes);
    }

    public async Task<TimesheetWriteResult> ApplyAsync(TimesheetUpdatePlan plan, CancellationToken ct = default)
    {
        var response = await _api.UpdateTimesheetAsync(plan.Request, ct);
        var timesheetId = plan.Request.TimeId!.Value;

        if (response is { Success: false })
        {
            return new TimesheetWriteResult
            {
                Success = false,
                TimesheetId = timesheetId,
                Message = response.Message
            };
        }

        return new TimesheetWriteResult
        {
            TimesheetId = timesheetId,
            Message = response?.Message,
            Timesheet = await TimesheetLookup.ReadByIdAsync(_api, plan.Request.EmpId, plan.Date, timesheetId, ct)
        };
    }

    public async Task<TimesheetWriteResult> UpdateAsync(
        int timesheetId,
        string employeeId,
        TimesheetUpdateOptions options,
        CancellationToken ct = default)
    {
        var plan = await PrepareAsync(timesheetId, employeeId, options, ct);
        return await ApplyAsync(plan, ct);
    }

    private async Task<int?> ResolveIterationAsync(
        TimesheetItem existing,
        string targetProjectId,
        TimesheetUpdateOptions options,
        CancellationToken ct)
    {
        if (options.Iteration is not null)
        {
            if (string.IsNullOrEmpty(targetProjectId))
                throw new TimesheetValidationException("An iteration can only be set once the project is known.");

            var available = await _api.GetIterationsAsync(targetProjectId, ct);
            if (available.Count == 0)
                throw new TimesheetValidationException(
                    $"Project '{targetProjectId}' does not use iterations.");

            return IterationResolver.ResolveByNameOrId(available, options.Iteration)
                ?? throw new TimesheetValidationException(
                    $"Unknown iteration '{options.Iteration}' for project '{targetProjectId}'. Available iterations: {IterationResolver.Describe(available)}.");
        }

        var projectChanged = options.ProjectId is not null
            && !string.Equals(options.ProjectId, existing.ProjectId, StringComparison.OrdinalIgnoreCase);
        var iterationId = projectChanged ? null : existing.IterationId;
        if (iterationId is not null || string.IsNullOrEmpty(targetProjectId))
            return iterationId;

        // The list endpoint usually returns the iteration name but not its ID, and SaveTimesheet
        // needs the ID in its full edit payload.
        var iterations = await _api.GetIterationsAsync(targetProjectId, ct);
        if (iterations.Count == 0)
            return null;

        return iterations
                   .FirstOrDefault(i => string.Equals(i.IterationName, existing.Iteration, StringComparison.OrdinalIgnoreCase))
                   ?.IterationId
               ?? throw new TimesheetValidationException(
                   $"Unable to resolve iteration '{existing.Iteration ?? "(none)"}' for project '{targetProjectId}'. Available iterations: {IterationResolver.Describe(iterations)}.");
    }

    private async Task<TimesheetSummaryEntry?> GetExistingEntryAsync(
        string empId, DateOnly date, int timesheetId, CancellationToken ct)
    {
        var filter = new TimesheetSummaryFilter
        {
            StartDate = date.ToString("yyyy-MM-dd"),
            EndDate = date.ToString("yyyy-MM-dd"),
            EmployeeIds = [empId]
        };

        var entries = await _api.QueryTimesheetsAsync(filter, ct);
        return entries.FirstOrDefault(e => e.TimeId == timesheetId);
    }

    private static List<string> DescribeChanges(TimesheetUpdateOptions options)
    {
        var changes = new List<string>();
        if (options.Location is not null) changes.Add($"Location -> {LocationResolver.Resolve(options.Location)}");
        if (options.Description is not null) changes.Add($"Description -> {Truncate(options.Description)}");
        if (options.Start is not null) changes.Add($"Start -> {options.Start}");
        if (options.End is not null) changes.Add($"End -> {options.End}");
        if (options.Less is not null) changes.Add($"Less -> {options.Less} minutes");
        if (options.ClientId is not null) changes.Add($"Client -> {options.ClientId}");
        if (options.ProjectId is not null) changes.Add($"Project -> {options.ProjectId}");
        if (options.Iteration is not null) changes.Add($"Iteration -> {options.Iteration}");
        if (options.Category is not null) changes.Add($"Category -> {options.Category}");
        if (options.Billable is not null) changes.Add($"Billable -> {options.Billable}");
        if (options.SellPrice is not null) changes.Add($"Sell price -> ${options.SellPrice:F2}");
        return changes;
    }

    private static string Truncate(string value) =>
        value.Length > 50 ? value[..50] + "..." : value;
}
