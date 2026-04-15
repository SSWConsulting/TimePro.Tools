using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Invoices;

[Description("List line items (products) on an invoice")]
public class LinesCommand : AsyncCommand<LinesCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<INVOICE_ID>")]
        [Description("Invoice ID")]
        public int InvoiceId { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public LinesCommand(ITimeProApiClient api, IConfigService config)
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
            var lines = await _api.GetInvoiceProductsAsync(settings.InvoiceId, CancellationToken.None);

            OutputHelper.Render(lines, settings.Json, rows =>
            {
                if (rows.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No line items.[/]");
                    return;
                }
                var table = new Table().Expand();
                table.AddColumn("Sku");
                table.AddColumn("Description");
                table.AddColumn(new TableColumn("Qty").RightAligned());
                table.AddColumn(new TableColumn("Sell").RightAligned());
                table.AddColumn(new TableColumn("Total").RightAligned());
                table.AddColumn(new TableColumn("Margin").RightAligned());

                decimal totalSell = 0;
                foreach (var l in rows)
                {
                    totalSell += l.SellTotal ?? 0;
                    table.AddRow(
                        Markup.Escape(l.SkuId ?? "?"),
                        Markup.Escape(l.ProdName ?? l.SkuName ?? l.Note ?? "-"),
                        $"{l.Qty:N2}",
                        $"${l.SellAmt:N2}",
                        $"${l.SellTotal:N2}",
                        $"${l.Margin:N2}");
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]Sum of line sell totals: [/][bold]${totalSell:N2}[/] [dim](ex-GST; compare to invoice header Sell total)[/]");
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
