using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared;
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
                throw new TimesheetValidationException(
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
            return Partial(plan, timesheetId, response?.Message, null);

        if (plan.Options.Iteration is null)
        {
            return new TimesheetWriteResult
            {
                TimesheetId = timesheetId,
                Message = response?.Message,
                Timesheet = await TimesheetLookup.ReadByIdAsync(_api, employeeId, plan.Date.Value, timesheetId.Value, ct)
            };
        }

        try
        {
            var updated = await _updates.UpdateAsync(
                timesheetId.Value,
                employeeId,
                new TimesheetUpdateOptions(
                    Iteration: plan.Options.Iteration,
                    Date: plan.Date.Value.ToString("yyyy-MM-dd")),
                ct);

            return updated.Success
                ? updated with { IterationApplied = true }
                : Partial(plan, timesheetId, response?.Message, updated.Message);
        }
        catch (Exception ex) when (ex is ApiException or TimesheetValidationException)
        {
            return Partial(plan, timesheetId, response?.Message, ex.Message);
        }
    }

    /// <summary>
    /// The suggestion was accepted but the requested iteration was not set. Accepting again would
    /// duplicate the entry, so the recovery is always to update the row that now exists.
    /// </summary>
    private static TimesheetWriteResult Partial(
        TimesheetAcceptPlan plan, int? timesheetId, string? message, string? reason)
    {
        if (plan.Options.Iteration is null)
            return new TimesheetWriteResult { TimesheetId = timesheetId, Message = message };

        var target = timesheetId?.ToString() ?? "<id>";
        var locate = timesheetId is null
            ? "Find it with 'tp ts get --week', then "
            : "";
        var warning =
            $"Timesheet accepted, but iteration '{plan.Options.Iteration}' was not applied. "
            + $"{locate}run: tp ts update {target} --iteration \"{plan.Options.Iteration}\". Do not accept again."
            + (reason is null ? "" : $" ({reason})");

        return new TimesheetWriteResult
        {
            TimesheetId = timesheetId,
            Message = message,
            IterationApplied = false,
            Warning = warning
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

        var suggestionHasIteration = suggestion.IterationId is not null
            || !string.IsNullOrEmpty(suggestion.Iteration);
        if (options.Iteration is null && suggestionHasIteration)
            return;

        var available = await _api.GetIterationsAsync(projectId, ct);

        if (options.Iteration is not null)
        {
            if (available.Count == 0)
                throw new TimesheetValidationException($"Project '{projectId}' does not use iterations.");

            if (IterationResolver.ResolveByNameOrId(available, options.Iteration) is null)
                throw new TimesheetValidationException(
                    $"Unknown iteration '{options.Iteration}' for project '{projectId}'. Available iterations: {IterationResolver.Describe(available)}.");

            return;
        }

        if (available.Count > 0 && !suggestionHasIteration)
            throw new TimesheetValidationException(
                $"Project '{projectId}' requires an iteration and the suggestion has none. Available iterations: {IterationResolver.Describe(available)}.");
    }

    /// <summary>
    /// Another session can add a row on the same day between prepare and read-back, so a new row is
    /// only the accepted one when it matches the suggestion. An ambiguous read resolves to nothing.
    /// </summary>
    private async Task<int?> FindAcceptedIdAsync(TimesheetAcceptPlan plan, string employeeId, CancellationToken ct)
    {
        if (plan.Date is null || plan.Suggestion is null)
            return null;

        try
        {
            var entries = await _api.GetTimesheetsAsync(employeeId, plan.Date.Value, ct);
            if (entries.Any(e => e.TimeId == plan.SuggestedId && !e.IsSuggested))
                return plan.SuggestedId;

            var candidates = entries
                .Where(e => !e.IsSuggested && !plan.ExistingIds.Contains(e.TimeId))
                .Where(e => Matches(e, plan))
                .ToList();

            if (candidates.Count > 1)
                candidates = candidates.Where(e => SameLocation(e, plan)).ToList();

            return candidates.Count == 1 ? candidates[0].TimeId : null;
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private static bool Matches(TimesheetItem candidate, TimesheetAcceptPlan plan)
    {
        var suggestion = plan.Suggestion!;
        var expectedNotes = (plan.Options.Notes ?? suggestion.Notes)?.Trim() ?? "";

        return string.Equals(candidate.ProjectId, suggestion.ProjectId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.ClientId, suggestion.ClientId, StringComparison.OrdinalIgnoreCase)
            && TimesheetLookup.SameClockTime(candidate.StartTime, suggestion.StartTime)
            && TimesheetLookup.SameClockTime(candidate.EndTime, suggestion.EndTime)
            && string.Equals(candidate.Notes?.Trim() ?? "", expectedNotes, StringComparison.Ordinal);
    }

    private static bool SameLocation(TimesheetItem candidate, TimesheetAcceptPlan plan)
    {
        var expected = plan.Options.Location is not null
            ? LocationResolver.Resolve(plan.Options.Location)
            : plan.Suggestion!.LocationId;

        return string.Equals(candidate.LocationId, expected, StringComparison.OrdinalIgnoreCase);
    }
}
