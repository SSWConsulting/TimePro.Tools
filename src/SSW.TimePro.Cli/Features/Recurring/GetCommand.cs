using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Recurring;

[Description("Get a recurring invoice template by ID")]
public class GetCommand : AsyncCommand<GetCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<RECURRING_ID>")]
        public int RecurringId { get; set; }

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    public GetCommand(ITimeProApiClient api, IConfigService config)
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
            var r = await _api.GetRecurringInvoiceAsync(settings.RecurringId, CancellationToken.None);
            if (r is null)
            {
                // Held recurring-invoice ID that doesn't resolve is a failed lookup — emit found:false for parsers, then fail.
                if (settings.Json)
                    OutputHelper.WriteJson(new { found = false, recurringId = settings.RecurringId });
                else
                    OutputHelper.WriteWarning($"Recurring invoice {settings.RecurringId} not found.");
                return 1;
            }

            OutputHelper.Render(r, settings.Json, d =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]ID[/]", d.Id?.ToString() ?? "-");
                table.AddRow("[bold]Client[/]", Markup.Escape(d.ClientId ?? "-"));
                table.AddRow("[bold]Unit[/]", d.Unit.ToString());
                table.AddRow("[bold]Periods[/]", $"first {d.FirstInvPeriod} · subsequent {d.SubsequentInvPeriod} · last {d.LastInvPeriod}");
                table.AddRow("[bold]Invoices generated[/]", d.CountOfInv.ToString());
                if (d.DateStart.HasValue) table.AddRow("[bold]Start[/]", d.DateStart.Value.ToString("yyyy-MM-dd"));
                table.AddRow("[bold]End[/]", d.DateEnd?.ToString("yyyy-MM-dd") ?? "open-ended");
                if (d.LastInvEndDate.HasValue) table.AddRow("[bold]Last invoiced to[/]", d.LastInvEndDate.Value.ToString("yyyy-MM-dd"));
                if (d.NextInvoicePeriodStart.HasValue || d.NextInvoicePeriodEnd.HasValue)
                    table.AddRow("[bold]Next period[/]",
                        $"{d.NextInvoicePeriodStart?.ToString("yyyy-MM-dd") ?? "?"} → {d.NextInvoicePeriodEnd?.ToString("yyyy-MM-dd") ?? "?"}");
                table.AddRow("[bold]Can generate now[/]",
                    d.CanGenerateNow ? "[green]yes[/]" : $"[dim]no{(string.IsNullOrWhiteSpace(d.CannotGenerateReason) ? "" : $" · {Markup.Escape(d.CannotGenerateReason)}")}[/]");
                if (d.SellAmt.HasValue) table.AddRow("[bold]Sell (ex tax)[/]", $"${d.SellAmt.Value:N2}");
                if (d.SellTaxAmt.HasValue) table.AddRow("[bold]Tax[/]", $"${d.SellTaxAmt.Value:N2}");
                if (d.SellTotal.HasValue) table.AddRow("[bold]Sell total[/]", $"${d.SellTotal.Value:N2}");
                if (!string.IsNullOrWhiteSpace(d.Note)) table.AddRow("[bold]Note[/]", Markup.Escape(d.Note));
                if (!string.IsNullOrWhiteSpace(d.NoteInternal)) table.AddRow("[bold]Internal note[/]", Markup.Escape(d.NoteInternal));
                AnsiConsole.Write(table);

                if (d.Products is { Count: > 0 })
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[bold]Products[/]");
                    var pt = new Table();
                    pt.AddColumn("Product");
                    pt.AddColumn("Name");
                    pt.AddColumn(new TableColumn("Qty").RightAligned());
                    pt.AddColumn(new TableColumn("Sell").RightAligned());
                    pt.AddColumn(new TableColumn("Total").RightAligned());
                    pt.AddColumn("Note");
                    foreach (var p in d.Products)
                    {
                        pt.AddRow(
                            Markup.Escape(p.ProdId ?? "-"),
                            Markup.Escape(p.ProdName ?? p.ProdCategoryName ?? "-"),
                            $"{p.Qty:N2}",
                            $"${p.SellAmt:N2}",
                            $"${p.SellTotal:N2}",
                            Markup.Escape(p.Note ?? "-"));
                    }
                    AnsiConsole.Write(pt);
                }
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
