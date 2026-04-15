using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Invoices;

[Description("List timesheets billed on (or written off against) an invoice")]
public class TimesheetsCommand : AsyncCommand<TimesheetsCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<INVOICE_ID>")]
        [Description("Invoice ID")]
        public int InvoiceId { get; set; }

        [CommandOption("--writeoff")]
        [Description("Show written-off timesheets instead of allocated")]
        public bool WriteOff { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public TimesheetsCommand(ITimeProApiClient api, IConfigService config)
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
            var type = settings.WriteOff ? "writeoff" : "allocated";
            var rows = await _api.GetInvoiceTimesheetsAsync(settings.InvoiceId, type, CancellationToken.None);

            OutputHelper.Render(rows, settings.Json, items =>
            {
                if (items.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No timesheets.[/]");
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
                AnsiConsole.MarkupLine($"[dim]Total: [/][bold]{hours:N2}h[/] [dim]·[/] [bold]${amount:N2}[/]");
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
