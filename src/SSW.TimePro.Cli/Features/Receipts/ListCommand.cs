using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Receipts;

[Description("List paid receipts (paged)")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--search <TEXT>")]
        [Description("Search text (client name, reference, etc.)")]
        public string? Search { get; set; }

        [CommandOption("--skip <N>")]
        [DefaultValue(0)]
        public int Skip { get; set; }

        [CommandOption("--limit <N>")]
        [DefaultValue(100)]
        public int Limit { get; set; }

        [CommandOption("--field <COL>")]
        [Description("Sort field (PaymentDate, DateCreated, Paid)")]
        [DefaultValue("PaymentDate")]
        public string Field { get; set; } = "PaymentDate";

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

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (_config.LoadActiveTenantConfig() is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var page = await _api.ListPaidReceiptsAsync(
                settings.Search, settings.Skip, settings.Limit, settings.Field, settings.Dir, CancellationToken.None);
            if (page is null)
            {
                OutputHelper.WriteWarning("No receipts returned.");
                return 0;
            }

            OutputHelper.Render(page, settings.Json, p =>
            {
                var table = new Table().Expand();
                table.AddColumn("Receipt");
                table.AddColumn("Payment date");
                table.AddColumn("Invoices");
                table.AddColumn("Client");
                table.AddColumn("Type");
                table.AddColumn(new TableColumn("Paid").RightAligned());
                table.AddColumn("Credit note");

                foreach (var r in p.Data)
                {
                    table.AddRow(
                        r.SaleReceiptId.ToString(),
                        r.PaymentDate?.ToString("yyyy-MM-dd") ?? "-",
                        r.InvoiceIds.Count == 0 ? "-" : string.Join(", ", r.InvoiceIds),
                        Markup.Escape(r.CoName ?? r.ClientId ?? "?"),
                        Markup.Escape(r.SaleReceiptType?.TypeName ?? "?"),
                        $"${Math.Abs(r.PaidTotal):N2}",
                        r.CreditNoteId is null ? "-" : $"[yellow]{r.CreditNoteId}[/]");
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]Showing {p.Data.Count} of {p.Total} receipts · PaidTotal is negative for incoming payments (shown as abs).[/]");
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
