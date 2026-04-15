using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Products;

[Description("List products (with optional SKU expansion) or all prepaid SKUs")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--expand")]
        [Description("Expand SKUs inline with each product")]
        public bool Expand { get; set; }

        [CommandOption("--prepaid")]
        [Description("List all prepaid SKUs (via /api/Product/All?IsPrepaid=true) instead of products")]
        public bool Prepaid { get; set; }

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    public ListCommand(ITimeProApiClient api, IConfigService config)
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
            if (settings.Prepaid)
            {
                var skus = await _api.ListAllSkusAsync(true, CancellationToken.None);
                OutputHelper.Render(skus, settings.Json, list =>
                {
                    var table = new Table().Expand();
                    table.AddColumn("SKU");
                    table.AddColumn("Name");
                    table.AddColumn("Product");
                    table.AddColumn(new TableColumn("Sell").RightAligned());
                    table.AddColumn(new TableColumn("Cost").RightAligned());
                    table.AddColumn("Prepaid?");
                    foreach (var s in list)
                    {
                        table.AddRow(
                            Markup.Escape(s.SkuId ?? "?"),
                            Markup.Escape(s.SkuName ?? "?"),
                            Markup.Escape(s.ProductId ?? "-"),
                            $"${s.SellAmt:N2}",
                            $"${s.CostAmt:N2}",
                            s.IsPrepaid == true ? "[yellow]yes[/]" : "-");
                    }
                    AnsiConsole.Write(table);
                    AnsiConsole.MarkupLine($"[dim]{list.Count} SKUs[/]");
                });
                return 0;
            }

            var rows = await _api.ListProductsAsync(settings.Expand, CancellationToken.None);
            OutputHelper.Render(rows, settings.Json, list =>
            {
                var table = new Table().Expand();
                table.AddColumn("Product ID");
                table.AddColumn("Name");
                table.AddColumn("Head");
                table.AddColumn("Training?");
                table.AddColumn("Allow discount?");
                if (settings.Expand) table.AddColumn("SKUs");
                foreach (var p in list)
                {
                    var columns = new List<string>
                    {
                        Markup.Escape(p.ProductId ?? "?"),
                        Markup.Escape(p.ProductName ?? "?"),
                        Markup.Escape(p.Head ?? "-"),
                        p.IsTraining ? "yes" : "-",
                        p.AllowDiscount ? "yes" : "-",
                    };
                    if (settings.Expand)
                        columns.Add((p.Skus?.Count ?? 0).ToString());
                    table.AddRow(columns.ToArray());
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]{list.Count} products[/]");
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
