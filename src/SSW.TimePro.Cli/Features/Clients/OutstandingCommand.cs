using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Clients;

[Description("List clients with unbilled time (outstanding)")]
public class OutstandingCommand : AsyncCommand<OutstandingCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    public OutstandingCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (_config.LoadActiveTenantConfig() is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var rows = await _api.GetClientsWithOutstandingTimeAsync(CancellationToken.None);

            OutputHelper.Render(rows, settings.Json, items =>
            {
                if (items.Count == 0)
                {
                    AnsiConsole.MarkupLine("[green]No clients with outstanding time.[/]");
                    return;
                }
                var table = new Table().Expand();
                table.AddColumn("Client");
                table.AddColumn("Account mgr");
                table.AddColumn(new TableColumn("Billable").RightAligned());
                table.AddColumn(new TableColumn("OS").RightAligned());
                table.AddColumn("Earliest unallocated");

                foreach (var c in items.OrderBy(c => c.EarliestUnAllocatedTimesheetDate))
                {
                    table.AddRow(
                        Markup.Escape($"{c.ClientId} · {c.CoName ?? "?"}"),
                        Markup.Escape(c.EmpId ?? "-"),
                        $"${c.Billable:N2}",
                        $"${c.Os:N2}",
                        c.EarliestUnAllocatedTimesheetDate.ToString("yyyy-MM-dd"));
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]{items.Count} clients with unbilled time[/]");
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
