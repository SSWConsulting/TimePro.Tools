using SSW.TimePro.Cli.Features.Rates;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Timesheets;

public sealed record TimesheetCreateOptions(
    string ClientId,
    string ProjectId,
    string? Date = null,
    string? Start = null,
    string? End = null,
    string? Description = null,
    string? Location = null,
    string? Category = null,
    int? IterationId = null,
    string? Billable = null,
    int? Less = null,
    decimal? SellPrice = null);

public sealed record TimesheetCreatePlan(
    TimesheetRequest Request,
    DateOnly Date,
    string Start,
    string End);

/// <summary>
/// A prepared create, or the one outcome the service refuses to resolve on its own: a client with
/// no active rate. <see cref="Plan"/> is null only in that case — everything else throws.
/// </summary>
public sealed record TimesheetCreatePreparation(TimesheetCreatePlan? Plan)
{
    public bool NoActiveRate => Plan is null;
}

/// <summary>
/// Builds a complete SaveTimesheet payload for a new entry: sell price from the client rate,
/// category from the repo mapping or recent entries, location from the WFH defaults, break time in
/// hours. The API rejects a row it cannot price, so a missing rate stops the create — creating a
/// rate is the caller's explicit decision, never a side effect of logging time.
/// </summary>
public sealed class TimesheetCreateService
{
    private const int RecentCategoryDays = 14;

    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public TimesheetCreateService(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    public async Task<TimesheetCreatePreparation> PrepareAsync(
        string employeeId,
        TimesheetCreateOptions options,
        CancellationToken ct = default)
    {
        if (options.Less is < 0)
            throw new TimesheetValidationException("Break/less time must be zero or greater");

        var date = options.Date is not null
            ? TimesheetLookup.ParseDate(options.Date)
            : DateOnly.FromDateTime(DateTime.Today);

        var start = options.Start ?? "09:00";
        var end = options.End ?? "17:00";
        var billableId = options.Billable ?? "B";
        var less = options.Less ?? 0;

        var sellPrice = options.SellPrice ?? await ResolveSellPriceAsync(
            employeeId, options.ClientId, billableId, date, ct);
        if (sellPrice is null)
            return new TimesheetCreatePreparation(null);

        var request = new TimesheetRequest
        {
            EmpId = employeeId,
            ClientId = options.ClientId,
            ProjectId = options.ProjectId,
            IterationId = options.IterationId,
            DateCreated = date.ToString("yyyy-MM-dd"),
            TimeStart = $"{date:yyyy-MM-dd}T{start}:00",
            TimeEnd = $"{date:yyyy-MM-dd}T{end}:00",
            TimeLess = less > 0 ? less / 60m : null,
            Note = options.Description,
            LocationId = ResolveLocation(options.Location, date),
            CategoryId = options.Category
                ?? CategoryFromRepoMapping(options.ClientId, options.ProjectId)
                ?? await CategoryFromRecentEntriesAsync(employeeId, options.ClientId, options.ProjectId, date, ct),
            BillableId = billableId,
            SellPrice = sellPrice,
        };

        return new TimesheetCreatePreparation(new TimesheetCreatePlan(request, date, start, end));
    }

    public async Task<TimesheetWriteResult> ApplyAsync(TimesheetCreatePlan plan, CancellationToken ct = default)
    {
        var response = await _api.CreateTimesheetAsync(plan.Request, ct);

        if (response is { Success: false })
        {
            return new TimesheetWriteResult
            {
                Success = false,
                TimesheetId = response.TimesheetId,
                Message = response.Message ?? "Failed to create timesheet"
            };
        }

        // SaveTimesheet usually answers with an empty body, so the row is read back, never re-sent.
        var created = response?.TimesheetId is not null
            ? await TimesheetLookup.ReadByIdAsync(_api, plan.Request.EmpId, plan.Date, response.TimesheetId.Value, ct)
            : await TimesheetLookup.ReadCreatedAsync(_api, plan.Request.EmpId, plan.Date, plan.Request, ct);

        return new TimesheetWriteResult
        {
            TimesheetId = response?.TimesheetId ?? created?.TimeId,
            Message = response?.Message,
            Timesheet = created
        };
    }

    private async Task<decimal?> ResolveSellPriceAsync(
        string employeeId, string clientId, string billableId, DateOnly date, CancellationToken ct)
    {
        var rate = await _api.GetClientRateAsync(employeeId, clientId, date, ct);
        var active = rate?.Rate is not null
            && (string.IsNullOrEmpty(rate.ExpiryDate) || RateResolver.IsActive(DateTime.Parse(rate.ExpiryDate), date));

        return active
            ? RateResolver.SellPriceFor(billableId, rate!.Rate ?? 0m, rate.PrepaidRate ?? 0m)
            : null;
    }

    private string ResolveLocation(string? requested, DateOnly date)
    {
        if (!string.IsNullOrEmpty(requested))
            return LocationResolver.Resolve(requested);

        var global = _config.LoadGlobalConfig();
        var location = global.WfhDays.Contains(date.DayOfWeek.ToString(), StringComparer.OrdinalIgnoreCase)
            ? "Home"
            : global.DefaultLocation;

        return LocationResolver.Resolve(location ?? "SSW");
    }

    private string? CategoryFromRepoMapping(string clientId, string projectId) =>
        _config.LoadRepoMappings()
            .FirstOrDefault(m =>
                string.Equals(m.ClientId, clientId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(m.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(m.CategoryId))
            ?.CategoryId;

    private async Task<string?> CategoryFromRecentEntriesAsync(
        string employeeId, string clientId, string projectId, DateOnly date, CancellationToken ct)
    {
        var filter = new TimesheetSummaryFilter
        {
            StartDate = date.AddDays(-RecentCategoryDays).ToString("yyyy-MM-dd"),
            EndDate = date.ToString("yyyy-MM-dd"),
            EmployeeIds = [employeeId],
            ClientIds = [clientId],
            ProjectIds = [projectId]
        };

        var entries = await _api.QueryTimesheetsAsync(filter, ct);
        return entries
            .Where(e => !string.IsNullOrEmpty(e.CategoryId))
            .OrderByDescending(e => e.TimesheetDate)
            .FirstOrDefault()
            ?.CategoryId;
    }
}
