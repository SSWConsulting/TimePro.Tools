using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Products;

[Description("Get a single product by ID")]
public class GetCommand : AsyncCommand<GetCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<PRODUCT_ID>")]
        [Description("Product ID")]
        public string ProductId { get; set; } = string.Empty;

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
            var p = await _api.GetProductAsync(settings.ProductId, CancellationToken.None);
            if (p is null)
            {
                OutputHelper.WriteWarning($"Product '{settings.ProductId}' not found.");
                return 1;
            }

            OutputHelper.Render(p, settings.Json, prod =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]Product ID[/]", Markup.Escape(prod.ProductId ?? "-"));
                table.AddRow("[bold]Name[/]", Markup.Escape(prod.ProductName ?? "-"));
                table.AddRow("[bold]Head[/]", Markup.Escape(prod.Head ?? "-"));
                table.AddRow("[bold]Training[/]", prod.IsTraining ? "yes" : "no");
                table.AddRow("[bold]Allow discount[/]", prod.AllowDiscount ? "yes" : "no");
                table.AddRow("[bold]Display on web[/]", prod.DisplayOnWeb ? "yes" : "no");
                if (!string.IsNullOrWhiteSpace(prod.Note))
                    table.AddRow("[bold]Note[/]", Markup.Escape(prod.Note));
                if (!string.IsNullOrWhiteSpace(prod.Url))
                    table.AddRow("[bold]URL[/]", Markup.Escape(prod.Url));
                AnsiConsole.Write(table);

                if (prod.Skus is { Count: > 0 })
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[bold]{prod.Skus.Count} SKUs[/]");
                    var skuTable = new Table();
                    skuTable.AddColumn("SKU");
                    skuTable.AddColumn("Name");
                    skuTable.AddColumn(new TableColumn("Sell").RightAligned());
                    skuTable.AddColumn(new TableColumn("Cost").RightAligned());
                    skuTable.AddColumn("Prepaid?");
                    foreach (var s in prod.Skus)
                    {
                        skuTable.AddRow(
                            Markup.Escape(s.SkuId ?? "?"),
                            Markup.Escape(s.SkuName ?? "?"),
                            $"${s.SellAmt:N2}",
                            $"${s.CostAmt:N2}",
                            s.IsPrepaid == true ? "[yellow]yes[/]" : "-");
                    }
                    AnsiConsole.Write(skuTable);
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
