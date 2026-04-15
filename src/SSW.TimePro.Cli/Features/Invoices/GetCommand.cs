using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Invoices;

[Description("Get invoice header details")]
public class GetCommand : AsyncCommand<GetCommand.Settings>
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

    public GetCommand(ITimeProApiClient api, IConfigService config)
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
            var inv = await _api.GetInvoiceAsync(settings.InvoiceId, CancellationToken.None);
            if (inv is null)
            {
                OutputHelper.WriteWarning($"Invoice {settings.InvoiceId} not found.");
                return 1;
            }

            OutputHelper.Render(inv, settings.Json, i =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]Invoice #[/]", i.InvoiceId.ToString());
                table.AddRow("[bold]Client[/]", Markup.Escape(i.ClientId ?? "?"));
                table.AddRow("[bold]Type[/]", Markup.Escape(i.InvoiceType ?? "?"));
                table.AddRow("[bold]Date created[/]", i.DateCreated?.ToString("yyyy-MM-dd") ?? "-");
                table.AddRow("[bold]Period[/]", $"{i.DateStart:yyyy-MM-dd} - {i.DateEnd:yyyy-MM-dd}");
                // paidAmt is stored negative (receipt sign convention); display as absolute.
                // salesTaxPct / marginPct come through as fractions (0.1 = 10%); multiply for display.
                table.AddRow("[bold]Sub total[/]", $"${i.SubTotal:N2}");
                table.AddRow("[bold]Sales tax[/]", $"${i.SalesTaxAmt:N2} ({(i.SalesTaxPct ?? 0) * 100:N2}%)");
                table.AddRow("[bold]Sell total[/]", $"${i.SellTotal:N2}");
                table.AddRow("[bold]Paid[/]", $"${Math.Abs(i.PaidAmt ?? 0):N2}");
                table.AddRow("[bold]Outstanding[/]", $"${i.OSAmt:N2}");
                table.AddRow("[bold]Cost total[/]", $"${i.CostTotal:N2}");
                table.AddRow("[bold]Margin[/]", $"${i.Margin:N2} ({(i.MarginPct ?? 0) * 100:N2}%)");
                table.AddRow("[bold]Locked[/]", i.IsLocked ? "[red]yes[/]" : "no");
                table.AddRow("[bold]Credit note[/]", i.IsCreditNote ? "[yellow]yes[/]" : "no");
                if (!string.IsNullOrEmpty(i.ExternalSyncId))
                    table.AddRow("[bold]External sync[/]", Markup.Escape($"{i.ExternalSyncType}: {i.ExternalSyncId} (status={i.ExternalSyncStatus})"));
                if (!string.IsNullOrWhiteSpace(i.Note))
                    table.AddRow("[bold]Note[/]", Markup.Escape(i.Note));
                if (!string.IsNullOrWhiteSpace(i.NoteInternal))
                    table.AddRow("[bold]Internal note[/]", Markup.Escape(i.NoteInternal));
                AnsiConsole.Write(table);
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
