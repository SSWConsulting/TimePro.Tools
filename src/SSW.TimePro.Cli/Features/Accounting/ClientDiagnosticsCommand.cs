using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Accounting;

[Description("Diagnose client invoices, aged debtors, unbilled time, credit notes, and rates")]
public class ClientDiagnosticsCommand : AsyncCommand<ClientDiagnosticsCommand.Settings>
{
    private readonly IAccountingDiagnosticsService _diagnostics;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--rates")]
        [Description("Include configured client rate evidence")]
        public bool IncludeRates { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ClientDiagnosticsCommand(IAccountingDiagnosticsService diagnostics, IConfigService config)
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
            var report = await _diagnostics.DiagnoseClientAccountingPositionAsync(
                settings.ClientId, settings.IncludeRates, cancellationToken);

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

    private static void RenderReport(ClientAccountingPositionDiagnostic report)
    {
        var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
        table.AddRow("[bold]Client[/]", Markup.Escape(report.ClientId));
        table.AddRow("[bold]Invoices[/]", $"{report.Totals.Invoices.Count} (${report.Totals.Invoices.SellTotalIncGst:N2} inc GST)");
        table.AddRow("[bold]Paid[/]", $"${report.Totals.Invoices.PaidAmt:N2}");
        table.AddRow("[bold]Outstanding[/]", $"${report.Totals.Invoices.OsAmt:N2}");
        table.AddRow("[bold]Aged debtors[/]", $"{report.Totals.AgedDebtors.Count} (${report.Totals.AgedDebtors.Total:N2})");
        table.AddRow("[bold]Unbilled[/]", $"{report.Totals.UnbilledTimesheets.Count} rows / {report.Totals.UnbilledTimesheets.Hours:N2}h / ${report.Totals.UnbilledTimesheets.ExGst:N2} ex GST");
        table.AddRow("[bold]Credit notes[/]", $"{report.Totals.CreditNotes.Count} (${report.Totals.CreditNotes.AbsoluteTotal:N2})");
        AnsiConsole.Write(table);

        foreach (var warning in report.Warnings)
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
    }
}
