using System.ComponentModel;
using SSW.TimePro.Cli.Features.Rates;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Create a new timesheet entry")]
public class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;
    private readonly TimesheetCreateService _creates;

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

        [CommandOption("--iteration <NAME_OR_ID>")]
        [Description("Iteration/sprint, by name or ID (required for projects that use iterations)")]
        public string? Iteration { get; set; }

        [CommandOption("--billable <TYPE>")]
        [Description("Billable type: B (billable), BPP (prepaid), W (write-off)")]
        public string? Billable { get; set; }

        [CommandOption("--less <MINUTES>")]
        [Description("Break/less time in whole minutes, e.g. --less 90")]
        public string? Less { get; set; }

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

    public CreateCommand(ITimeProApiClient api, IConfigService config, TimesheetCreateService creates)
    {
        _api = api;
        _config = config;
        _creates = creates;
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

        if (!LessOption.TryParse(settings.Less, out var lessMinutes, out var lessError))
        {
            if (settings.Json)
                OutputHelper.WriteJsonError(lessError!);
            else
                OutputHelper.WriteError(lessError!);
            return 1;
        }

        var options = new TimesheetCreateOptions(
            ClientId: settings.ClientId,
            ProjectId: settings.ProjectId,
            Date: settings.Date,
            Start: settings.Start,
            End: settings.End,
            Description: settings.Description,
            Location: settings.Location,
            Category: settings.Category,
            Iteration: settings.Iteration,
            Billable: settings.Billable,
            Less: lessMinutes);

        try
        {
            var prepared = await _creates.PrepareAsync(tenant.EmployeeId, options, cancellationToken);

            if (prepared.NoActiveRate)
            {
                var sellPrice = await ResolveMissingRateAsync(
                    tenant.EmployeeId, settings.ClientId, settings.Billable ?? "B",
                    settings.Yes, settings.Json, settings.RejectIfRateExpired, cancellationToken);
                if (sellPrice is null)
                    return 1; // rejected, user cancelled, or non-interactive with no rate set

                prepared = await _creates.PrepareAsync(
                    tenant.EmployeeId, options with { SellPrice = sellPrice }, cancellationToken);
            }

            var plan = prepared.Plan!;

            if (!settings.Json)
                WritePreview(plan, settings);

            if (!settings.Yes && !settings.Json && !AnsiConsole.Confirm("Create this timesheet?"))
                return 1;

            var result = await _creates.ApplyAsync(plan, cancellationToken);

            if (!result.Success)
            {
                var failure = result.Message ?? "Failed to create timesheet";
                if (settings.Json)
                    OutputHelper.WriteJsonError(failure);
                else
                    OutputHelper.WriteError(failure);
                return 1;
            }

            if (settings.Json)
                OutputHelper.WriteJson(result);
            else
                OutputHelper.WriteSuccess($"Timesheet created{(result.TimesheetId is not null ? $" (ID: {result.TimesheetId})" : "")}");

            return 0;
        }
        catch (TimesheetValidationException ex)
        {
            if (settings.Json)
                OutputHelper.WriteJsonError(ex.Message);
            else
                OutputHelper.WriteError(ex.Message);
            return 1;
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

    private static void WritePreview(TimesheetCreatePlan plan, Settings settings)
    {
        var request = plan.Request;
        AnsiConsole.MarkupLine("[bold]Creating timesheet:[/]");
        AnsiConsole.MarkupLine($"  Date:     {plan.Date:yyyy-MM-dd} ({plan.Date:dddd})");
        AnsiConsole.MarkupLine($"  Client:   {Markup.Escape(request.ClientId)}");
        AnsiConsole.MarkupLine($"  Project:  {Markup.Escape(request.ProjectId)}");
        AnsiConsole.MarkupLine($"  Time:     {plan.Start} - {plan.End}");
        AnsiConsole.MarkupLine($"  Location: {Markup.Escape(request.LocationId ?? "?")}");
        AnsiConsole.MarkupLine($"  Billable: {request.BillableId}");
        if (request.CategoryId is not null)
            AnsiConsole.MarkupLine($"  Category: {Markup.Escape(request.CategoryId)}{(settings.Category is null ? " [dim](auto-resolved)[/]" : "")}");
        if (request.SellPrice is not null)
            AnsiConsole.MarkupLine($"  Sell price: ${request.SellPrice:F2}");
        if (!string.IsNullOrEmpty(request.Note))
            AnsiConsole.MarkupLine($"  Notes:    {Markup.Escape(request.Note)}");
        AnsiConsole.WriteLine();
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
}
