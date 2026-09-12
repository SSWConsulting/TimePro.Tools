using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Invoices;

[Description("List receipts/payments against an invoice")]
public class ReceiptsCommand : AsyncCommand<ReceiptsCommand.Settings>
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

    public ReceiptsCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (_config.LoadActiveTenantConfig() is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var rows = await _api.GetInvoiceReceiptsAsync(settings.InvoiceId, CancellationToken.None);

            OutputHelper.Render(rows, settings.Json, items =>
            {
                if (items.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No receipts.[/]");
                    return;
                }
                var table = new Table().Expand();
                table.AddColumn("Receipt #");
                table.AddColumn("Payment date");
                table.AddColumn("Status");
                table.AddColumn(new TableColumn("Paid").RightAligned());
                table.AddColumn("Prepaid?");

                decimal total = 0;
                foreach (var r in items)
                {
                    total += r.Paid;
                    table.AddRow(
                        r.SaleReceiptId.ToString(),
                        r.PaymentDate?.ToString("yyyy-MM-dd") ?? "-",
                        Markup.Escape(r.SaleReceiptStatus ?? "?"),
                        $"${Math.Abs(r.Paid):N2}",
                        r.IsCreditingPrepaid == true ? "[yellow]yes[/]" : "-");
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]Total (absolute): [/][bold]${Math.Abs(total):N2}[/] [dim](Paid is negative for incoming payments)[/]");
            });

            return 0;
        }
        catch (ApiException ex)
        {
            if (settings.Json)
                OutputHelper.WriteJsonError($"API error: {ex.Message}", ex.StatusCode);
            else
                OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
