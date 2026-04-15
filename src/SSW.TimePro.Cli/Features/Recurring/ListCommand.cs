using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Recurring;

[Description("List recurring invoice templates")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT_ID>")]
        [Description("Filter to a specific client")]
        public string? ClientId { get; set; }

        [CommandOption("--query <TEXT>")]
        public string? Query { get; set; }

        [CommandOption("--outdated")]
        [Description("Include outdated (inactive or stopped) recurring invoices")]
        public bool ShowOutdated { get; set; }

        [CommandOption("--skip <N>")]
        [DefaultValue(0)]
        public int Skip { get; set; }

        [CommandOption("--limit <N>")]
        [DefaultValue(50)]
        public int Limit { get; set; }

        [CommandOption("--field <COL>")]
        [DefaultValue("LastInvEndDate")]
        public string Field { get; set; } = "LastInvEndDate";

        [CommandOption("--dir <DIR>")]
        [DefaultValue("desc")]
        public string Dir { get; set; } = "desc";

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

        try
        {
            var page = await _api.ListRecurringInvoicesAsync(
                settings.Query, settings.ClientId, settings.ShowOutdated,
                settings.Skip, settings.Limit, settings.Field, settings.Dir, CancellationToken.None);
            if (page is null)
            {
                OutputHelper.WriteWarning("No recurring invoices returned.");
                return 0;
            }

            OutputHelper.Render(page, settings.Json, p =>
            {
                var table = new Table().Expand();
                table.AddColumn("ID");
                table.AddColumn("Client");
                table.AddColumn("Unit");
                table.AddColumn(new TableColumn("Sell total").RightAligned());
                table.AddColumn(new TableColumn("# inv").RightAligned());
                table.AddColumn("Last end");
                table.AddColumn("Active?");
                table.AddColumn("Note");

                foreach (var r in p.Data)
                {
                    table.AddRow(
                        r.Id?.ToString() ?? "-",
                        Markup.Escape($"{r.ClientId} · {r.ClientName ?? "?"}"),
                        Markup.Escape(r.Unit ?? "-"),
                        $"${r.SellTotal:N2}",
                        r.CountOfInv.ToString(),
                        r.LastInvEndDate?.ToString("yyyy-MM-dd") ?? "-",
                        r.IsActive ? "[green]yes[/]" : "[dim]no[/]",
                        Markup.Escape(r.Note ?? "-"));
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]Showing {p.Data.Count} of {p.Total} recurring templates[/]");
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
