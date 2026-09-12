using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Receipts;

[Description("Get receipt details with allocations")]
public class GetCommand : AsyncCommand<GetCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<RECEIPT_ID>")]
        [Description("Receipt ID")]
        public int ReceiptId { get; set; }

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
            var r = await _api.GetReceiptDetailAsync(settings.ReceiptId, CancellationToken.None);
            if (r?.Receipt is null)
            {
                // Held receipt ID that doesn't resolve is a failed lookup — emit found:false for parsers, then fail.
                if (settings.Json)
                    OutputHelper.WriteJson(new { found = false, receiptId = settings.ReceiptId });
                else
                    OutputHelper.WriteWarning($"Receipt {settings.ReceiptId} not found.");
                return 1;
            }

            OutputHelper.Render(r, settings.Json, v =>
            {
                var d = v.Receipt!;
                var head = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                head.AddRow("[bold]Receipt #[/]", d.SaleReceiptId.ToString());
                head.AddRow("[bold]Client[/]", Markup.Escape($"{d.ClientId} · {d.CoName ?? "?"}"));
                head.AddRow("[bold]Payment date[/]", d.PaymentDate?.ToString("yyyy-MM-dd") ?? "-");
                head.AddRow("[bold]Type[/]", Markup.Escape(TypeName(v, d.ReceiptType)));
                head.AddRow("[bold]Total[/]", $"${Math.Abs(d.ReceiptTotal):N2}");
                if (!string.IsNullOrWhiteSpace(d.BatchNo))
                    head.AddRow("[bold]Batch[/]", Markup.Escape(d.BatchNo));
                if (!string.IsNullOrWhiteSpace(v.ContactPerson))
                    head.AddRow("[bold]Contact[/]", Markup.Escape(v.ContactPerson));
                if (!string.IsNullOrWhiteSpace(d.ExternalSyncType))
                    head.AddRow("[bold]Synced to[/]", Markup.Escape(d.ExternalSyncType));
                if (!string.IsNullOrWhiteSpace(d.Note))
                    head.AddRow("[bold]Note[/]", Markup.Escape(d.Note));
                AnsiConsole.Write(head);

                if (d.SaleReceiptPaids.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[bold]Allocations[/]");
                    var alloc = new Table();
                    alloc.AddColumn("Invoice");
                    alloc.AddColumn("Date invoiced");
                    alloc.AddColumn("Status");
                    alloc.AddColumn(new TableColumn("Paid").RightAligned());
                    alloc.AddColumn(new TableColumn("Invoice total").RightAligned());
                    alloc.AddColumn(new TableColumn("Balance").RightAligned());
                    foreach (var a in d.SaleReceiptPaids)
                    {
                        alloc.AddRow(
                            a.InvoiceId.ToString(),
                            a.InvoiceDate?.ToString("yyyy-MM-dd") ?? "-",
                            Markup.Escape(a.SaleReceiptStatus ?? "-"),
                            $"${Math.Abs(a.PaidAmt):N2}",
                            $"${a.Total:N2}",
                            $"${a.Balance:N2}");
                    }
                    AnsiConsole.Write(alloc);
                }
            });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteApiError(ex, settings.Json);
            return 1;
        }
    }

    private static string TypeName(ReceiptDetailResponse v, string? typeId) =>
        v.PaymentMethods.FirstOrDefault(m => m.Id == typeId)?.Name ?? typeId ?? "?";
}
