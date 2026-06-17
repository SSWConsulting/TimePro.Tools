using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("List leave entries")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandOption("--filter <FILTER>")]
        [Description("Filter: UPCOMING (default) or PAST")]
        public string Filter { get; set; } = "UPCOMING";

        [CommandOption("--limit <N>")]
        [Description("Number of results (default: 10)")]
        public int Limit { get; set; } = 10;

        [CommandOption("--emp-id|--employee-id|--employee <EMP_ID>")]
        [Description("empId. Defaults to all visible leave")]
        public string? EmpId { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ListCommand(ITimeProApiClient api) => _api = api;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _api.GetLeaveAsync(
                settings.Filter.ToUpperInvariant(), 1, settings.Limit, settings.EmpId, CancellationToken.None);

            var items = response?.Leaves?.Items ?? [];

            OutputHelper.Render(items, settings.Json, list =>
            {
                if (list.Count == 0)
                {
                    OutputHelper.WriteInfo("No leave entries found");
                    return;
                }

                var table = new Table()
                    .AddColumn("ID")
                    .AddColumn("Type")
                    .AddColumn("Start")
                    .AddColumn("End")
                    .AddColumn("Hours")
                    .AddColumn("Status")
                    .AddColumn("Note");

                foreach (var l in list)
                {
                    var start = l.StartDate?.Split('T')[0] ?? "?";
                    var end = l.EndDate?.Split('T')[0] ?? "?";
                    var status = l.StatusName;
                    var statusColor = l.LeaveStatus switch
                    {
                        2 or 3 or 4 => "green",
                        5 or 7 => "red",
                        _ => "yellow"
                    };

                    table.AddRow(
                        Markup.Escape(l.Id[..8]),
                        Markup.Escape(l.LeaveType?.Name ?? "?"),
                        start,
                        end,
                        $"{l.TotalHours:0.0}",
                        $"[{statusColor}]{status}[/]",
                        Markup.Escape(l.Note ?? ""));
                }

                AnsiConsole.Write(table);
            });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
