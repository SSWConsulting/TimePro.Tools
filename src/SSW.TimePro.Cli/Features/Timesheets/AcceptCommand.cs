using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Accept a suggested timesheet")]
public class AcceptCommand : AsyncCommand<AcceptCommand.Settings>
{
    private readonly ITimeProApiClient _api;

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

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public AcceptCommand(ITimeProApiClient api)
    {
        _api = api;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes && !settings.Json)
        {
            if (!AnsiConsole.Confirm($"Accept suggested timesheet #{settings.SuggestedId}?"))
                return 1;
        }

        try
        {
            var response = await _api.AcceptSuggestedTimesheetAsync(
                settings.SuggestedId,
                settings.Location,
                settings.Notes,
                null,
                CancellationToken.None);

            if (settings.Json)
            {
                OutputHelper.WriteJson(response ?? new TimesheetResponse { Success = true });
            }
            else if (response is null || response.Success)
            {
                // API returns empty body on success — treat null as success
                OutputHelper.WriteSuccess(
                    $"Suggested timesheet accepted{(response?.TimesheetId is not null ? $" (new ID: {response.TimesheetId})" : "")}");
            }
            else
            {
                OutputHelper.WriteError(response.Message ?? "Failed to accept suggested timesheet");
                return 1;
            }

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            var detail = ApiErrorParser.ExtractDetail(ex.ResponseBody);
            if (detail is not null)
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(detail)}[/]");
            return 1;
        }
    }
}
