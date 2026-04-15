using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Products;

[Description("Show product discounts for a client")]
public class DiscountsCommand : AsyncCommand<DiscountsCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    public DiscountsCommand(ITimeProApiClient api, IConfigService config)
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
            OutputHelper.WriteError("--client is required.");
            return 1;
        }

        try
        {
            var rows = await _api.GetProductDiscountsForClientAsync(settings.ClientId, CancellationToken.None);

            OutputHelper.Render(rows, settings.Json, items =>
            {
                if (items.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No discounts configured for this client.[/]");
                    return;
                }
                var table = new Table().Expand();
                table.AddColumn("Product");
                table.AddColumn("SKU");
                table.AddColumn(new TableColumn("Discount %").RightAligned());
                table.AddColumn(new TableColumn("Discount amt").RightAligned());
                table.AddColumn("From");
                table.AddColumn("To");
                table.AddColumn("Note");

                foreach (var d in items)
                {
                    var pct = d.DiscountPct.HasValue ? $"{d.DiscountPct * 100:N2}%" : "-";
                    table.AddRow(
                        Markup.Escape(d.ProductId ?? "-"),
                        Markup.Escape(d.SkuId ?? "-"),
                        pct,
                        $"${d.DiscountAmt:N2}",
                        d.DateStart?.ToString("yyyy-MM-dd") ?? "-",
                        d.DateEnd?.ToString("yyyy-MM-dd") ?? "-",
                        Markup.Escape(d.Note ?? "-"));
                }
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
