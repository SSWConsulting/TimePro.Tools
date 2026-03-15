using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Create a leave request")]
public class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandOption("--start <DATE>")]
        [Description("Start date (yyyy-MM-dd)")]
        public string Start { get; set; } = string.Empty;

        [CommandOption("--end <DATE>")]
        [Description("End date (yyyy-MM-dd)")]
        public string End { get; set; } = string.Empty;

        [CommandOption("--type <TYPE>")]
        [Description("Leave type ID or name (e.g., 1, 'Annual Leave')")]
        public string Type { get; set; } = string.Empty;

        [CommandOption("--note <NOTE>")]
        [Description("Leave note/reason")]
        public string? Note { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public CreateCommand(ITimeProApiClient api) => _api = api;

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Start) || string.IsNullOrEmpty(settings.End) || string.IsNullOrEmpty(settings.Type))
        {
            OutputHelper.WriteError("--start, --end, and --type are required");
            return 1;
        }

        try
        {
            // Resolve type ID
            var leaveTypeId = await ResolveLeaveTypeAsync(settings.Type);
            if (leaveTypeId is null)
            {
                OutputHelper.WriteError($"Unknown leave type: '{settings.Type}'. Use 'tp leave types' or a numeric ID.");
                var types = await _api.GetLeaveTypesAsync(CancellationToken.None);
                foreach (var t in types.Where(t => t.IsActive))
                    AnsiConsole.MarkupLine($"  {t.Id}: {Markup.Escape(t.Name)}");
                return 1;
            }

            if (!settings.Yes && !settings.Json)
            {
                AnsiConsole.MarkupLine($"[bold]Creating leave request:[/]");
                AnsiConsole.MarkupLine($"  Start: {settings.Start}");
                AnsiConsole.MarkupLine($"  End:   {settings.End}");
                AnsiConsole.MarkupLine($"  Type:  {settings.Type}");
                if (!string.IsNullOrEmpty(settings.Note))
                    AnsiConsole.MarkupLine($"  Note:  {Markup.Escape(settings.Note)}");
                AnsiConsole.WriteLine();

                if (!AnsiConsole.Confirm("Submit this leave request?"))
                    return 1;
            }

            var request = new CreateLeaveRequest
            {
                StartDate = settings.Start,
                EndDate = settings.End,
                LeaveTypeId = leaveTypeId.Value,
                Note = settings.Note,
                AllDay = true
            };

            await _api.CreateLeaveAsync(request, CancellationToken.None);

            if (settings.Json)
                OutputHelper.WriteJson(new { success = true });
            else
                OutputHelper.WriteSuccess("Leave request created");

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private async Task<int?> ResolveLeaveTypeAsync(string typeInput)
    {
        if (int.TryParse(typeInput, out var id))
            return id;

        var types = await _api.GetLeaveTypesAsync(CancellationToken.None);
        var match = types.FirstOrDefault(t =>
            t.Name.Equals(typeInput, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }
}
