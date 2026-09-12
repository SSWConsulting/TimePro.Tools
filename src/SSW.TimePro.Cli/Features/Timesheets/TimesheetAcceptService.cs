using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Timesheets;

public sealed record TimesheetAcceptOptions(
    string? Location = null,
    string? Notes = null,
    string? Iteration = null,
    string? Date = null);

public sealed record TimesheetAcceptPlan(
    int SuggestedId,
    TimesheetItem? Suggestion,
    DateOnly? Date,
    IReadOnlyList<int> ExistingIds,
    TimesheetAcceptOptions Options);

/// <summary>
/// Accepting a suggestion. AcceptSuggestedTimesheet takes no iteration, so an iteration is applied
/// afterwards through <see cref="TimesheetUpdateService"/> on the entry the accept produced.
/// </summary>
public sealed class TimesheetAcceptService
{
    private readonly ITimeProApiClient _api;
    private readonly TimesheetUpdateService _updates;

    public TimesheetAcceptService(ITimeProApiClient api, TimesheetUpdateService updates)
    {
        _api = api;
        _updates = updates;
    }

    public async Task<TimesheetAcceptPlan> PrepareAsync(
        int suggestedId,
        string employeeId,
        TimesheetAcceptOptions options,
        CancellationToken ct = default)
    {
        var found = await TimesheetLookup.FindAsync(_api, employeeId, suggestedId, options.Date, ct);
        if (found is null)
        {
            if (options.Iteration is not null)
                throw new TimesheetUpdateValidationException(
                    $"Suggested timesheet #{suggestedId} not found. Pass the date (yyyy-MM-dd) to narrow the search.");

            return new TimesheetAcceptPlan(suggestedId, null, null, [], options);
        }

        var suggestion = found.Value.Item;
        var day = found.Value.Day;
        await ValidateIterationAsync(suggestion, options, ct);

        return new TimesheetAcceptPlan(
            suggestedId,
            suggestion,
            day.Date,
            day.Entries.Select(e => e.TimeId).ToList(),
            options);
    }

    public async Task<TimesheetWriteResult> ApplyAsync(
        TimesheetAcceptPlan plan,
        string employeeId,
        CancellationToken ct = default)
    {
        var response = await _api.AcceptSuggestedTimesheetAsync(
            plan.SuggestedId, plan.Options.Location, plan.Options.Notes, null, ct);

        if (response is { Success: false })
            return new TimesheetWriteResult { Success = false, Message = response.Message };

        var timesheetId = response?.TimesheetId ?? await FindAcceptedIdAsync(plan, employeeId, ct);
        if (timesheetId is null || plan.Date is null)
            return new TimesheetWriteResult { TimesheetId = timesheetId, Message = response?.Message };

        if (plan.Options.Iteration is not null)
        {
            return await _updates.UpdateAsync(
                timesheetId.Value,
                employeeId,
                new TimesheetUpdateOptions(
                    Iteration: plan.Options.Iteration,
                    Date: plan.Date.Value.ToString("yyyy-MM-dd")),
                ct);
        }

        return new TimesheetWriteResult
        {
            TimesheetId = timesheetId,
            Message = response?.Message,
            Timesheet = await TimesheetLookup.ReadByIdAsync(_api, employeeId, plan.Date.Value, timesheetId.Value, ct)
        };
    }

    public async Task<TimesheetWriteResult> AcceptAsync(
        int suggestedId,
        string employeeId,
        TimesheetAcceptOptions options,
        CancellationToken ct = default)
    {
        var plan = await PrepareAsync(suggestedId, employeeId, options, ct);
        return await ApplyAsync(plan, employeeId, ct);
    }

    private async Task ValidateIterationAsync(
        TimesheetItem suggestion,
        TimesheetAcceptOptions options,
        CancellationToken ct)
    {
        var projectId = suggestion.ProjectId;
        if (string.IsNullOrEmpty(projectId))
            return;

        var available = await _api.GetIterationsAsync(projectId, ct);

        if (options.Iteration is not null)
        {
            if (available.Count == 0)
                throw new TimesheetUpdateValidationException($"Project '{projectId}' does not use iterations.");

            if (IterationResolver.ResolveByNameOrId(available, options.Iteration) is null)
                throw new TimesheetUpdateValidationException(
                    $"Unknown iteration '{options.Iteration}' for project '{projectId}'. Available iterations: {IterationResolver.Describe(available)}.");

            return;
        }

        var suggestionHasIteration = suggestion.IterationId is not null
            || !string.IsNullOrEmpty(suggestion.Iteration);
        if (available.Count > 0 && !suggestionHasIteration)
            throw new TimesheetUpdateValidationException(
                $"Project '{projectId}' requires an iteration and the suggestion has none. Available iterations: {IterationResolver.Describe(available)}.");
    }

    private async Task<int?> FindAcceptedIdAsync(TimesheetAcceptPlan plan, string employeeId, CancellationToken ct)
    {
        if (plan.Date is null)
            return null;

        try
        {
            var entries = await _api.GetTimesheetsAsync(employeeId, plan.Date.Value, ct);
            if (entries.Any(e => e.TimeId == plan.SuggestedId && !e.IsSuggested))
                return plan.SuggestedId;

            return entries
                .Where(e => !e.IsSuggested && !plan.ExistingIds.Contains(e.TimeId))
                .Select(e => (int?)e.TimeId)
                .LastOrDefault();
        }
        catch (ApiException)
        {
            return null;
        }
    }
}
