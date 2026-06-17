using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Prepaid;

[Description("Show structured prepaid drawdown totals for an invoice")]
public class SummaryCommand : AsyncCommand<SummaryCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<INVOICE_ID>")]
        [Description("Prepaid invoice ID")]
        public int InvoiceId { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public SummaryCommand(ITimeProApiClient api, IConfigService config)
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
            var summary = await _api.GetPrepaidStatusSummaryAsync(settings.InvoiceId, CancellationToken.None);
            if (summary is null)
            {
                OutputHelper.WriteWarning($"Invoice {settings.InvoiceId} not found.");
                return 1;
            }

            if (!string.Equals(summary.InvoiceType, "Prepaid", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.Json)
                {
                    OutputHelper.WriteJson(new
                    {
                        error = $"Invoice {settings.InvoiceId} is not a prepaid invoice.",
                        invoiceId = settings.InvoiceId,
                        invoiceType = summary.InvoiceType
                    });
                }
                else
                {
                    OutputHelper.WriteWarning($"Invoice {settings.InvoiceId} is type '{summary.InvoiceType ?? "unknown"}', not Prepaid.");
                }

                return 1;
            }

            OutputHelper.Render(summary, settings.Json, s =>
            {
                var head = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                head.AddRow("[bold]Invoice #[/]", s.InvoiceId.ToString());
                head.AddRow("[bold]Client[/]", Markup.Escape(s.ClientId ?? "?"));
                head.AddRow("[bold]Type[/]", Markup.Escape(s.InvoiceType ?? "?"));
                var salesTaxPct = s.SalesTaxPct ?? 0;
                head.AddRow("[bold]Tax rate[/]", $"{(salesTaxPct > 1 ? salesTaxPct : salesTaxPct * 100):N2}%");
                AnsiConsole.Write(head);

                AnsiConsole.WriteLine();
                var table = new Table().Expand();
                table.AddColumn("Total");
                table.AddColumn(new TableColumn("Ex GST").RightAligned());
                table.AddColumn(new TableColumn("GST").RightAligned());
                table.AddColumn(new TableColumn("In GST").RightAligned());

                AddMoneyRow(table, "Original", s.Original);
                AddMoneyRow(table, "Drawn down", s.DrawnDown);
                AddMoneyRow(table, "Credited", s.Credited);
                AddMoneyRow(table, "Remaining", s.Remaining);
                AnsiConsole.Write(table);

                AnsiConsole.MarkupLine(
                    $"[dim]{s.DrawdownTimesheetCount} prepaid timesheets, {s.CreditingCreditNoteCount} credit notes[/]");

                if (s.ReconciliationDeltaExGst != 0 || s.ReconciliationDeltaIncGst != 0)
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow]Reconciliation delta:[/] ex GST ${s.ReconciliationDeltaExGst:N3}, in GST ${s.ReconciliationDeltaIncGst:N3}");
                }
            });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private static void AddMoneyRow(Table table, string label, TaxBreakdown value)
    {
        table.AddRow(
            label,
            $"${value.ExGst:N3}",
            $"${value.Gst:N3}",
            $"${value.IncGst:N3}");
    }
}
