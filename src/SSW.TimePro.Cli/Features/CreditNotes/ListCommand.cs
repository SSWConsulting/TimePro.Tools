using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.CreditNotes;

[Description("List credit notes for a client")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
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

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            OutputHelper.WriteError("--client is required.");
            return 1;
        }

        try
        {
            var rows = await _api.GetCreditNotesByClientAsync(settings.ClientId, CancellationToken.None);

            OutputHelper.Render(rows, settings.Json, items =>
            {
                if (items.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No credit notes for this client.[/]");
                    return;
                }
                var table = new Table().Expand();
                table.AddColumn("CN #");
                table.AddColumn("Date");
                table.AddColumn(new TableColumn("Amount").RightAligned());
                table.AddColumn(new TableColumn("Paid").RightAligned());
                table.AddColumn("Locked");
                table.AddColumn("Linked invoice");
                table.AddColumn("Sync");
                table.AddColumn("Note");

                decimal total = 0;
                foreach (var c in items.OrderBy(c => c.CreditNoteDate))
                {
                    total += c.Amount;
                    table.AddRow(
                        c.Id.ToString(),
                        c.CreditNoteDate.ToString("yyyy-MM-dd"),
                        $"${c.Amount:N2}",
                        $"${c.Paid:N2}",
                        c.IsLocked ? "[red]yes[/]" : "no",
                        c.AssociatedInvoiceId?.ToString() ?? "-",
                        Markup.Escape(c.SyncDisplayName ?? "-"),
                        Markup.Escape(c.Note ?? "-"));
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]Total credit note amount: [/][bold]${total:N2}[/]");
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
