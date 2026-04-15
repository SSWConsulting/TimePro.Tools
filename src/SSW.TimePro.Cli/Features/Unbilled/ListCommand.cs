using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Unbilled;

[Description("List unbilled (unallocated) timesheets for a client")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--page-size <N>")]
        public int? PageSize { get; set; }

        [CommandOption("--skip <N>")]
        public int? Skip { get; set; }

        [CommandOption("--field <COL>")]
        [Description("Sort field (e.g. DateCreated)")]
        public string? SortField { get; set; }

        [CommandOption("--dir <DIR>")]
        public string? Direction { get; set; }

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    public ListCommand(ITimeProApiClient api, IConfigService config)
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

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            OutputHelper.WriteError("--client is required.");
            return 1;
        }

        try
        {
            var rows = await _api.GetUnallocatedTimesheetsByClientAsync(
                settings.ClientId, settings.PageSize, settings.Skip, settings.SortField, settings.Direction,
                CancellationToken.None);

            OutputHelper.Render(rows, settings.Json, items =>
            {
                if (items.Count == 0)
                {
                    AnsiConsole.MarkupLine("[green]No unbilled time for this client.[/]");
                    return;
                }
                var table = new Table().Expand();
                table.AddColumn("Date");
                table.AddColumn("Employee");
                table.AddColumn("Project");
                table.AddColumn("Billable");
                table.AddColumn(new TableColumn("Hours").RightAligned());
                table.AddColumn(new TableColumn("Amount").RightAligned());

                decimal hours = 0, amount = 0;
                foreach (var t in items)
                {
                    hours += t.TotalTime ?? 0;
                    amount += t.BillableAmount ?? t.Amount ?? 0;
                    table.AddRow(
                        t.DateCreated?.ToString("yyyy-MM-dd") ?? "-",
                        Markup.Escape(t.EmpName ?? t.EmpId ?? "?"),
                        Markup.Escape(t.ProjectName ?? t.ProjectId ?? "?"),
                        Markup.Escape(t.BillableId ?? "?"),
                        $"{t.TotalTime:N2}",
                        $"${t.BillableAmount ?? t.Amount:N2}");
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]Unbilled: [/][bold]{hours:N2}h[/] [dim]·[/] [bold]${amount:N2}[/]");
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
