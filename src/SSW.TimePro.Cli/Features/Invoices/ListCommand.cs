using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Invoices;

[Description("List invoices (paged, filtered)")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--query <TEXT>")]
        [Description("Search text (company name, invoice id, etc.)")]
        public string? Query { get; set; }

        [CommandOption("--skip <N>")]
        [Description("Rows to skip (default 0)")]
        [DefaultValue(0)]
        public int Skip { get; set; }

        [CommandOption("--limit <N>")]
        [Description("Page size (default 50)")]
        [DefaultValue(50)]
        public int Limit { get; set; }

        [CommandOption("--field <COL>")]
        [Description("Sort field (e.g. DateCreated, DateInvoiced, SellTotal, ClientID)")]
        [DefaultValue("DateCreated")]
        public string Field { get; set; } = "DateCreated";

        [CommandOption("--dir <DIR>")]
        [Description("Sort direction: asc | desc")]
        [DefaultValue("desc")]
        public string Dir { get; set; } = "desc";

        [CommandOption("--recurring")]
        [Description("Show only recurring-generated invoices")]
        public bool Recurring { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
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
            var page = await _api.ListInvoicesAsync(
                settings.Query, settings.Skip, settings.Limit,
                settings.Field, settings.Dir, settings.Recurring, CancellationToken.None);

            if (page is null)
            {
                OutputHelper.WriteWarning("No invoices returned.");
                return 0;
            }

            OutputHelper.Render(page, settings.Json, p =>
            {
                var table = new Table().Expand();
                table.AddColumn("InvID");
                table.AddColumn("Date");
                table.AddColumn("Client");
                table.AddColumn("Type");
                table.AddColumn(new TableColumn("Sell").RightAligned());
                table.AddColumn(new TableColumn("Paid").RightAligned());
                table.AddColumn(new TableColumn("OS").RightAligned());

                foreach (var row in p.Data)
                {
                    // paidAmt follows the receipt sign convention — stored as negative for
                    // money received. Display as absolute, compute OS as sell + paid (sell − |paid|).
                    var paidAbs = Math.Abs(row.PaidAmt);
                    var os = row.SellTotal - paidAbs;
                    table.AddRow(
                        row.InvoiceId.ToString(),
                        row.DateCreated.ToString("yyyy-MM-dd"),
                        Markup.Escape($"{row.ClientId} · {row.CoName ?? "?"}"),
                        Markup.Escape(row.InvoiceType ?? "?"),
                        $"${row.SellTotal:N2}",
                        $"${paidAbs:N2}",
                        os > 0 ? $"[yellow]${os:N2}[/]" : $"[dim]${os:N2}[/]");
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]Showing {p.Data.Count} of {p.Total} invoices (skip={settings.Skip}, limit={settings.Limit})[/]");
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
