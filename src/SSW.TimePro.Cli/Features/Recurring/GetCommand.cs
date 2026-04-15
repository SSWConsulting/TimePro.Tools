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

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
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
                OutputHelper.WriteWarning($"Recurring invoice {settings.RecurringId} not found.");
                return 1;
            }

            OutputHelper.Render(r, settings.Json, d =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]ID[/]", d.Id.ToString());
                table.AddRow("[bold]Client[/]", Markup.Escape($"{d.ClientId} · {d.ClientName ?? "?"}"));
                table.AddRow("[bold]Unit[/]", Markup.Escape(d.Unit ?? "-"));
                if (d.Interval.HasValue) table.AddRow("[bold]Interval[/]", d.Interval.Value.ToString());
                if (d.DayOfMonth.HasValue) table.AddRow("[bold]Day of month[/]", d.DayOfMonth.Value.ToString());
                if (d.NextInvoiceDate.HasValue) table.AddRow("[bold]Next invoice[/]", d.NextInvoiceDate.Value.ToString("yyyy-MM-dd"));
                if (d.StartDate.HasValue) table.AddRow("[bold]Start[/]", d.StartDate.Value.ToString("yyyy-MM-dd"));
                if (d.EndDate.HasValue) table.AddRow("[bold]End[/]", d.EndDate.Value.ToString("yyyy-MM-dd"));
                if (d.SellTotal.HasValue) table.AddRow("[bold]Sell total[/]", $"${d.SellTotal.Value:N2}");
                if (d.TaxRate.HasValue) table.AddRow("[bold]Tax rate[/]", $"{d.TaxRate.Value:N2}%");
                table.AddRow("[bold]Active[/]", d.IsActive ? "yes" : "no");
                if (!string.IsNullOrWhiteSpace(d.Note)) table.AddRow("[bold]Note[/]", Markup.Escape(d.Note));
                if (!string.IsNullOrWhiteSpace(d.NoteInternal)) table.AddRow("[bold]Internal note[/]", Markup.Escape(d.NoteInternal));
                AnsiConsole.Write(table);

                if (d.Products is { Count: > 0 })
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[bold]Products[/]");
                    var pt = new Table();
                    pt.AddColumn("SKU");
                    pt.AddColumn("Name");
                    pt.AddColumn(new TableColumn("Qty").RightAligned());
                    pt.AddColumn(new TableColumn("Sell").RightAligned());
                    pt.AddColumn(new TableColumn("Total").RightAligned());
                    foreach (var p in d.Products)
                    {
                        pt.AddRow(
                            Markup.Escape(p.SkuId ?? "-"),
                            Markup.Escape(p.ProductName ?? p.SkuName ?? "-"),
                            $"{p.Qty:N2}",
                            $"${p.SellAmt:N2}",
                            $"${p.SellTotal:N2}");
                    }
                    AnsiConsole.Write(pt);
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
}
