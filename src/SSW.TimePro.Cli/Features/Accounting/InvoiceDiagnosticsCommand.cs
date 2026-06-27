using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Accounting;

[Description("Diagnose invoice totals, timesheets, receipts, write-offs, and credit notes")]
public class InvoiceDiagnosticsCommand : AsyncCommand<InvoiceDiagnosticsCommand.Settings>
{
    private readonly IAccountingDiagnosticsService _diagnostics;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<INVOICE_ID>")]
        [Description("Invoice ID")]
        public int InvoiceId { get; set; }

        [CommandOption("--no-writeoffs")]
        [Description("Skip written-off timesheet evidence")]
        public bool NoWriteOffs { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public InvoiceDiagnosticsCommand(IAccountingDiagnosticsService diagnostics, IConfigService config)
    {
        _diagnostics = diagnostics;
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
            var report = await _diagnostics.DiagnoseInvoiceReconciliationAsync(
                settings.InvoiceId, includeWriteOffs: !settings.NoWriteOffs, cancellationToken);
            if (report is null)
            {
                if (settings.Json)
                    OutputHelper.WriteJson(new { found = false, invoiceId = settings.InvoiceId });
                else
                    OutputHelper.WriteWarning($"Invoice {settings.InvoiceId} not found.");
                return 1;
            }

            OutputHelper.Render(report, settings.Json, RenderReport);
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

    private static void RenderReport(InvoiceReconciliationDiagnostic report)
    {
        var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
        table.AddRow("[bold]Invoice[/]", report.InvoiceId.ToString());
        table.AddRow("[bold]Client[/]", Markup.Escape(report.Invoice.ClientId ?? "-"));
        table.AddRow("[bold]Sub total ex GST[/]", $"${report.Invoice.SubTotalExGst:N2}");
        table.AddRow("[bold]GST[/]", $"${report.Invoice.Gst:N2}");
        table.AddRow("[bold]Sell total inc GST[/]", $"${report.Invoice.SellTotalIncGst:N2}");
        table.AddRow("[bold]Paid[/]", $"${report.Invoice.PaidAmt:N2}");
        table.AddRow("[bold]Outstanding[/]", $"${report.Invoice.OsAmt:N2}");
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine($"[dim]Lines:[/] {report.Totals.Lines.Count}; [dim]allocated timesheets:[/] {report.Totals.AllocatedTimesheets.Count}; [dim]receipts:[/] {report.Totals.Receipts.Count}; [dim]credit notes:[/] {report.Totals.RelatedCreditNotes.Count}");
        foreach (var warning in report.Warnings)
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
    }
}
