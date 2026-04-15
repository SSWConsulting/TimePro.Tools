using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Receipts;

[Description("Aged-debtor view for a client: outstanding invoices with days overdue")]
public class OutstandingCommand : AsyncCommand<OutstandingCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    public OutstandingCommand(ITimeProApiClient api, IConfigService config)
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

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            OutputHelper.WriteError("<CLIENT_ID> is required.");
            return 1;
        }

        try
        {
            var data = await _api.GetClientOutstandingAsync(settings.ClientId, CancellationToken.None);
            if (data is null)
            {
                OutputHelper.WriteWarning($"No outstanding data for client '{settings.ClientId}'.");
                return 0;
            }

            OutputHelper.Render(data, settings.Json, d =>
            {
                AnsiConsole.MarkupLine($"[bold]{Markup.Escape(d.CoName ?? d.ClientId ?? "?")}[/] ({Markup.Escape(d.ClientId ?? "-")})");
                if (d.OutstandingInvoices is null or { Count: 0 })
                {
                    AnsiConsole.MarkupLine("[green]No outstanding invoices.[/]");
                    return;
                }
                var table = new Table().Expand();
                table.AddColumn("Invoice");
                table.AddColumn("Date invoiced");
                table.AddColumn("Due");
                table.AddColumn(new TableColumn("Total").RightAligned());
                table.AddColumn(new TableColumn("Paid").RightAligned());
                table.AddColumn(new TableColumn("OS").RightAligned());
                table.AddColumn(new TableColumn("Days overdue").RightAligned());

                decimal totalOs = 0;
                foreach (var i in d.OutstandingInvoices)
                {
                    totalOs += i.OsAmt ?? 0;
                    var days = i.DaysOverdue ?? 0;
                    var daysStr = days switch
                    {
                        <= 0 => $"[dim]{days}[/]",
                        <= 30 => $"[yellow]{days}[/]",
                        _ => $"[red]{days}[/]"
                    };
                    table.AddRow(
                        i.InvoiceId.ToString(),
                        i.DateInvoiced?.ToString("yyyy-MM-dd") ?? "-",
                        i.DueDate?.ToString("yyyy-MM-dd") ?? "-",
                        $"${i.Total:N2}",
                        $"${i.PaidAmt:N2}",
                        $"${i.OsAmt:N2}",
                        daysStr);
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]Outstanding: [/][bold]${totalOs:N2}[/]");
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
