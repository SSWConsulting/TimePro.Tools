using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Accept a suggested timesheet")]
public class AcceptCommand : AsyncCommand<AcceptCommand.Settings>
{
    private readonly IConfigService _config;
    private readonly TimesheetAcceptService _accepts;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<SUGGESTED_ID>")]
        [Description("Suggested timesheet ID to accept")]
        public int SuggestedId { get; set; }

        [CommandOption("--location <LOC>")]
        [Description("Override location")]
        public string? Location { get; set; }

        [CommandOption("--notes <NOTES>")]
        [Description("Override notes")]
        public string? Notes { get; set; }

        [CommandOption("--iteration <NAME_OR_ID>")]
        [Description("Iteration/sprint to set on the accepted timesheet, by name or ID")]
        public string? Iteration { get; set; }

        [CommandOption("--date <DATE>")]
        [Description("Date the suggestion is on (yyyy-MM-dd). Defaults to searching recent weeks.")]
        public string? Date { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public AcceptCommand(IConfigService config, TimesheetAcceptService accepts)
    {
        _config = config;
        _accepts = accepts;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            const string message = "Not logged in. Run 'tp login --tenant <id>' first.";
            if (settings.Json)
                OutputHelper.WriteJsonError(message);
            else
                OutputHelper.WriteError(message);
            return 1;
        }

        var options = new TimesheetAcceptOptions(
            Location: settings.Location,
            Notes: settings.Notes,
            Iteration: settings.Iteration,
            Date: settings.Date);

        try
        {
            var plan = await _accepts.PrepareAsync(
                settings.SuggestedId, tenant.EmployeeId, options, cancellationToken);

            if (!settings.Yes && !settings.Json)
            {
                if (!AnsiConsole.Confirm($"Accept suggested timesheet #{settings.SuggestedId}?"))
                    return 1;
            }

            var result = await _accepts.ApplyAsync(plan, tenant.EmployeeId, cancellationToken);

            if (!result.Success)
            {
                var message = result.Message ?? "Failed to accept suggested timesheet";
                if (settings.Json)
                    OutputHelper.WriteJsonError(message);
                else
                    OutputHelper.WriteError(message);
                return 1;
            }

            if (settings.Json)
                OutputHelper.WriteJson(result);
            else
                OutputHelper.WriteSuccess(
                    $"Suggested timesheet accepted{(result.TimesheetId is not null ? $" (new ID: {result.TimesheetId})" : "")}");

            if (result.Warning is not null)
            {
                if (!settings.Json)
                    OutputHelper.WriteWarning(result.Warning);
                return 1;
            }

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
}
