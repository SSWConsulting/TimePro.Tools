using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Features.Rates;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Create a new timesheet entry")]
public class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--project <PROJECT>")]
        [Description("Project ID")]
        public string ProjectId { get; set; } = string.Empty;

        [CommandOption("--date <DATE>")]
        [Description("Date (yyyy-MM-dd). Defaults to today")]
        public string? Date { get; set; }

        [CommandOption("--start <TIME>")]
        [Description("Start time (HH:mm). Defaults to 09:00")]
        public string? Start { get; set; }

        [CommandOption("--end <TIME>")]
        [Description("End time (HH:mm). Defaults to 17:00")]
        public string? End { get; set; }

        [CommandOption("--description <DESC>")]
        [Description("Notes/description")]
        public string? Description { get; set; }

        [CommandOption("--location <LOC>")]
        [Description("Location (e.g., Office, Home, Client)")]
        public string? Location { get; set; }

        [CommandOption("--category <CAT>")]
        [Description("Category ID")]
        public string? Category { get; set; }

        [CommandOption("--iteration <ID>")]
        [Description("Iteration/sprint ID")]
        public int? Iteration { get; set; }

        [CommandOption("--billable <TYPE>")]
        [Description("Billable type: B (billable), BPP (prepaid), W (write-off)")]
        public string? Billable { get; set; }

        [CommandOption("--less <MINUTES>")]
        [Description("Break/less time in minutes")]
        public int? Less { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--reject-if-rate-expired")]
        [Description("Fail (with recovery guidance) instead of creating a rate when the client rate is expired or not set")]
        public bool RejectIfRateExpired { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public CreateCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.ClientId) || string.IsNullOrEmpty(settings.ProjectId))
        {
            OutputHelper.WriteError("--client and --project are required");
            return 1;
        }

        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        var date = settings.Date is not null
            ? DateOnly.ParseExact(settings.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Today);

        var startTime = settings.Start ?? "09:00";
        var endTime = settings.End ?? "17:00";
        var lessMins = settings.Less ?? 0;

        // Resolve location from WFH defaults if not specified
        var location = settings.Location;
        if (string.IsNullOrEmpty(location))
        {
            var global = _config.LoadGlobalConfig();
            var dayName = date.DayOfWeek.ToString();
            location = global.WfhDays.Contains(dayName, StringComparer.OrdinalIgnoreCase)
                ? "Home"
                : global.DefaultLocation;
        }

        location = LocationResolver.Resolve(location ?? "SSW");

        var billableId = settings.Billable ?? "B";

        try
        {
            // Resolve the sell price from the client rate. When no active rate exists the API rejects
            // the timesheet (it can't derive a sell price), so offer to set one before continuing.
            var rate = await _api.GetClientRateAsync(
                tenant.EmployeeId, settings.ClientId, date, CancellationToken.None);

            var rateActive = rate?.Rate is not null
                && (string.IsNullOrEmpty(rate.ExpiryDate) || RateResolver.IsActive(DateTime.Parse(rate.ExpiryDate), date));

            decimal? sellPrice;
            if (rateActive)
            {
                sellPrice = RateResolver.SellPriceFor(billableId, rate!.Rate ?? 0m, rate.PrepaidRate ?? 0m);
            }
            else
            {
                sellPrice = await ResolveMissingRateAsync(
                    tenant.EmployeeId, settings.ClientId, billableId,
                    settings.Yes, settings.Json, settings.RejectIfRateExpired, cancellationToken);
                if (sellPrice is null)
                    return 1; // rejected, user cancelled, or non-interactive with no rate set
            }

            // Auto-resolve category when not explicitly specified
            var categoryId = settings.Category
                ?? ResolveCategoryFromRepoMapping(settings.ClientId, settings.ProjectId)
                ?? await ResolveCategoryFromRecentTimesheets(
                    tenant.EmployeeId, settings.ClientId, settings.ProjectId, date);

            var request = new TimesheetRequest
            {
                EmpId = tenant.EmployeeId,
                ClientId = settings.ClientId,
                ProjectId = settings.ProjectId,
                IterationId = settings.Iteration,
                DateCreated = date.ToString("yyyy-MM-dd"),
                TimeStart = $"{date:yyyy-MM-dd}T{startTime}:00",
                TimeEnd = $"{date:yyyy-MM-dd}T{endTime}:00",
                TimeLess = lessMins > 0 ? lessMins / 60m : null,
                Note = settings.Description,
                LocationId = location,
                CategoryId = categoryId,
                BillableId = billableId,
                SellPrice = sellPrice,
            };

            // Show preview
            if (!settings.Json)
            {
                AnsiConsole.MarkupLine("[bold]Creating timesheet:[/]");
                AnsiConsole.MarkupLine($"  Date:     {date:yyyy-MM-dd} ({date:dddd})");
                AnsiConsole.MarkupLine($"  Client:   {Markup.Escape(settings.ClientId)}");
                AnsiConsole.MarkupLine($"  Project:  {Markup.Escape(settings.ProjectId)}");
                AnsiConsole.MarkupLine($"  Time:     {startTime} - {endTime}");
                AnsiConsole.MarkupLine($"  Location: {Markup.Escape(location ?? "?")}");
                AnsiConsole.MarkupLine($"  Billable: {request.BillableId}");
                if (categoryId is not null)
                    AnsiConsole.MarkupLine($"  Category: {Markup.Escape(categoryId)}{(settings.Category is null ? " [dim](auto-resolved)[/]" : "")}");
                if (sellPrice is not null)
                    AnsiConsole.MarkupLine($"  Sell price: ${sellPrice:F2}");
                if (!string.IsNullOrEmpty(settings.Description))
                    AnsiConsole.MarkupLine($"  Notes:    {Markup.Escape(settings.Description)}");
                AnsiConsole.WriteLine();
            }

            if (!settings.Yes && !settings.Json)
            {
                if (!AnsiConsole.Confirm("Create this timesheet?"))
                    return 1;
            }

            var response = await _api.CreateTimesheetAsync(request, CancellationToken.None);

            if (settings.Json)
            {
                OutputHelper.WriteJson(response ?? new TimesheetResponse { Success = true });
            }
            else if (response is null || response.Success)
            {
                // API returns empty body on success
                OutputHelper.WriteSuccess($"Timesheet created{(response?.TimesheetId is not null ? $" (ID: {response.TimesheetId})" : "")}");
            }
            else
            {
                OutputHelper.WriteError(response.Message ?? "Failed to create timesheet");
                return 1;
            }

            return 0;
        }
        catch (ApiException ex)
        {
            var detail = ApiErrorParser.ExtractDetail(ex.ResponseBody);
            if (settings.Json)
            {
                OutputHelper.WriteJsonError($"API error: {ex.Message}", ex.StatusCode, detail);
                return 1;
            }
            if (detail is not null && detail.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                OutputHelper.WriteError("A timesheet already exists for this time slot.");
                OutputHelper.WriteInfo("Use 'tp ts update <ID> --description \"...\"' to update the existing entry.");
            }
            else if (detail is not null && detail.Contains("category", StringComparison.OrdinalIgnoreCase))
            {
                OutputHelper.WriteError($"API requires a category. Pass --category <ID> or add categoryId to repo-mappings.json.");
                OutputHelper.WriteInfo("Hint: check existing timesheets with 'tp query --client <ID> --from <date> --to <date>'");
            }
            else
            {
                OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
                if (detail is not null)
                    AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(detail)}[/]");
            }
            return 1;
        }
    }

    /// <summary>
    /// No active rate exists for the client (expired or never set), which the API needs to derive a
    /// sell price. Mirrors the Angular timesheet form: interactively offer to create a rate inline
    /// (amount defaulting to the recommended one, or typed). Returns the resolved sell price, or null
    /// to abort. When non-interactive (<paramref name="yes"/> / <paramref name="json"/>) or
    /// <paramref name="rejectIfExpired"/> is set, it doesn't create anything — it returns a
    /// machine-actionable recovery recipe and aborts.
    /// </summary>
    private async Task<decimal?> ResolveMissingRateAsync(
        string empId, string clientId, string billableId, bool yes, bool json, bool rejectIfExpired, CancellationToken ct)
    {
        // Fail fast on an explicit reject — no recommendation lookup, no extra API call.
        if (rejectIfExpired)
        {
            RateGuard.ReportNoActiveRate(clientId, new RateRecommendation(0m, 0m, RateSource.None), json);
            return null;
        }

        var init = await _api.InitializeClientRateAsync(empId, clientId, ct);
        var rec = init is not null ? RateResolver.Recommend(init) : new RateRecommendation(0m, 0m, RateSource.None);

        // Non-interactive: can't prompt — report the recovery recipe (with recommended amounts) and abort.
        if (yes || json)
        {
            RateGuard.ReportNoActiveRate(clientId, rec, json);
            return null;
        }

        OutputHelper.WriteWarning($"No rate is set (or it has expired) for client '{clientId}'.");
        if (!AnsiConsole.Confirm($"Create a rate for '{clientId}' now?"))
            return null;

        // Create a rate inline, defaulting to the recommended amount (previous, else employee
        // default) — the same choices the Angular dialog offers. Expiry is left to the API default;
        // use 'tp rate update' to change it.
        var rate = AnsiConsole.Prompt(new TextPrompt<decimal>($"Regular rate (recommended ${rec.Rate:F2}):")
            .DefaultValue(rec.Rate).ShowDefaultValue());
        var prepaid = AnsiConsole.Prompt(new TextPrompt<decimal>($"Prepaid rate (recommended ${rec.PrepaidRate:F2}):")
            .DefaultValue(rec.PrepaidRate).ShowDefaultValue());

        await _api.SaveClientRateAsync(new SaveClientRateModel
        {
            EmpId = empId,
            ClientId = clientId,
            Rate = rate,
            PrepaidRate = prepaid,
            ExpiryDate = null
        }, ct);
        OutputHelper.WriteSuccess($"Rate created: ${rate:F2}.");
        return RateResolver.SellPriceFor(billableId, rate, prepaid);
    }

    /// <summary>
    /// Look up categoryId from repo-mappings.json for the given client/project.
    /// </summary>
    private string? ResolveCategoryFromRepoMapping(string clientId, string projectId)
    {
        var mappings = _config.LoadRepoMappings();
        var match = mappings.FirstOrDefault(m =>
            string.Equals(m.ClientId, clientId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.ProjectId, projectId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(m.CategoryId));
        return match?.CategoryId;
    }

    /// <summary>
    /// Look up categoryId from recent timesheets for the same employee + client + project.
    /// Searches the past 14 days for a match.
    /// </summary>
    private async Task<string?> ResolveCategoryFromRecentTimesheets(
        string empId, string clientId, string projectId, DateOnly aroundDate)
    {
        var filter = new TimesheetSummaryFilter
        {
            StartDate = aroundDate.AddDays(-14).ToString("yyyy-MM-dd"),
            EndDate = aroundDate.ToString("yyyy-MM-dd"),
            EmployeeIds = [empId],
            ClientIds = [clientId],
            ProjectIds = [projectId]
        };

        var entries = await _api.QueryTimesheetsAsync(filter, CancellationToken.None);
        return entries
            .Where(e => !string.IsNullOrEmpty(e.CategoryId))
            .OrderByDescending(e => e.TimesheetDate)
            .FirstOrDefault()
            ?.CategoryId;
    }
}
