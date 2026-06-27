using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Accounting;

[Description("Find non-zero timesheets with 0% tax on non-zero-tax invoices")]
public class TaxMismatchesCommand : AsyncCommand<TaxMismatchesCommand.Settings>
{
    private readonly IAccountingDiagnosticsService _diagnostics;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--query <TEXT>")]
        [Description("Optional invoice search text, such as a client id or invoice id")]
        public string? Query { get; set; }

        [CommandOption("--skip <N>")]
        [DefaultValue(0)]
        public int Skip { get; set; }

        [CommandOption("--limit <N>")]
        [DefaultValue(500)]
        public int Limit { get; set; } = 500;

        [CommandOption("--field <COL>")]
        [DefaultValue("DateCreated")]
        public string Field { get; set; } = "DateCreated";

        [CommandOption("--dir <DIR>")]
        [DefaultValue("desc")]
        public string Dir { get; set; } = "desc";

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    public TaxMismatchesCommand(IAccountingDiagnosticsService diagnostics, IConfigService config)
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
            var report = await _diagnostics.FindTimesheetTaxMismatchesAsync(
                settings.Query, settings.Skip, settings.Limit, settings.Field, settings.Dir, cancellationToken);

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

    private static void RenderReport(TimesheetTaxMismatchReport report)
    {
        AnsiConsole.MarkupLine($"[bold]Mismatches:[/] {report.MismatchCount}");
        AnsiConsole.MarkupLine($"[dim]Scanned {report.ScannedInvoiceCount} invoices; {report.InvoicesWithNonZeroTax} had non-zero tax.[/]");

        if (report.Rows.Count == 0)
            return;

        var table = new Table().Expand();
        table.AddColumn("Invoice");
        table.AddColumn("Time");
        table.AddColumn("Emp");
        table.AddColumn("Project");
        table.AddColumn(new TableColumn("Amount ex GST").RightAligned());
        table.AddColumn(new TableColumn("Invoice tax").RightAligned());
        table.AddColumn(new TableColumn("Timesheet tax").RightAligned());

        foreach (var row in report.Rows)
        {
            table.AddRow(
                row.InvoiceId.ToString(),
                row.TimeId.ToString(),
                Markup.Escape(row.EmpId ?? "-"),
                Markup.Escape(row.ProjectId ?? "-"),
                $"${row.AmountExGst:N2}",
                row.InvoiceTaxPct.HasValue ? $"{row.InvoiceTaxPct:N2}%" : "-",
                row.TimesheetTaxPct.HasValue ? $"{row.TimesheetTaxPct:N2}%" : "-");
        }

        AnsiConsole.Write(table);
    }
}
