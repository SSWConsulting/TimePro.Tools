using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Update an existing timesheet entry")]
public class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly IConfigService _config;
    private readonly TimesheetUpdateService _updates;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Timesheet ID to update")]
        public int TimesheetId { get; set; }

        [CommandOption("--location <LOC>")]
        [Description("New location (SSW, Home, Client, Travel, Other)")]
        public string? Location { get; set; }

        [CommandOption("--description <DESC>")]
        [Description("New notes/description")]
        public string? Description { get; set; }

        [CommandOption("--start <TIME>")]
        [Description("New start time (HH:mm)")]
        public string? Start { get; set; }

        [CommandOption("--end <TIME>")]
        [Description("New end time (HH:mm)")]
        public string? End { get; set; }

        [CommandOption("--less <MINUTES>")]
        [Description("New break/less time in whole minutes, e.g. --less 90. Use 0 to clear it")]
        public string? Less { get; set; }

        [CommandOption("--client <CLIENT>")]
        [Description("New client ID")]
        public string? ClientId { get; set; }

        [CommandOption("--project <PROJECT>")]
        [Description("New project ID")]
        public string? ProjectId { get; set; }

        [CommandOption("--iteration <NAME_OR_ID>")]
        [Description("New iteration/sprint, by name or ID")]
        public string? Iteration { get; set; }

        [CommandOption("--category <CAT>")]
        [Description("New category ID")]
        public string? Category { get; set; }

        [CommandOption("--billable <TYPE>")]
        [Description("New billable type: B, BPP, or W")]
        public string? Billable { get; set; }

        [CommandOption("--sell-price <AMOUNT>")]
        [Description("Override this timesheet's sell price (its own snapshot, independent of the client rate). Unchanged if omitted.")]
        public decimal? SellPrice { get; set; }

        [CommandOption("--date <DATE>")]
        [Description("Date the timesheet is on (yyyy-MM-dd). Used to look up the existing entry. Defaults to searching recent weeks.")]
        public string? Date { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public UpdateCommand(IConfigService config, TimesheetUpdateService updates)
    {
        _config = config;
        _updates = updates;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
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

        var options = new TimesheetUpdateOptions(
            Location: settings.Location,
            Description: settings.Description,
            Start: settings.Start,
            End: settings.End,
            Less: lessMinutes,
            ClientId: settings.ClientId,
            ProjectId: settings.ProjectId,
            Category: settings.Category,
            Billable: settings.Billable,
            SellPrice: settings.SellPrice,
            Iteration: settings.Iteration,
            Date: settings.Date);

        try
        {
            var plan = await _updates.PrepareAsync(
                settings.TimesheetId, tenant.EmployeeId, options, cancellationToken);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[bold]Updating timesheet #{settings.TimesheetId}:[/]");
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(plan.Existing.Client ?? "?")} | {Markup.Escape(plan.Existing.Project ?? "?")} ({plan.Date:yyyy-MM-dd})[/]");
                foreach (var change in plan.Changes)
                    AnsiConsole.MarkupLine($"  {Markup.Escape(change)}");
                AnsiConsole.WriteLine();

                if (!settings.Yes && !AnsiConsole.Confirm("Apply these changes?"))
                    return 1;
            }

            var result = await _updates.ApplyAsync(plan, cancellationToken);

            if (!result.Success)
            {
                var message = result.Message ?? "Failed to update timesheet";
                if (settings.Json)
                    OutputHelper.WriteJsonError(message);
                else
                    OutputHelper.WriteError(message);
                return 1;
            }

            if (settings.Json)
                OutputHelper.WriteJson(result);
            else
                OutputHelper.WriteSuccess($"Timesheet #{settings.TimesheetId} updated");

            return 0;
        }
        catch (TimesheetUpdateValidationException ex)
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
                OutputHelper.WriteJsonError($"API error: {ex.Message}", ex.StatusCode, detail);
            else
                OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
