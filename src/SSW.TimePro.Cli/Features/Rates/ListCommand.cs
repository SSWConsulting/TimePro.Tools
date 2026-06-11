using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Rates;

[Description("List configured client rates (paged table)")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--emp-id|--employee-id|--employee <EMP_ID>")]
        [Description("Filter to one empId")]
        public string? EmpId { get; set; }

        [CommandOption("--show-expired")]
        public bool ShowExpired { get; set; }

        [CommandOption("--page-size <N>")]
        [DefaultValue(100)]
        public int PageSize { get; set; }

        [CommandOption("--skip <N>")]
        [DefaultValue(0)]
        public int Skip { get; set; }

        [CommandOption("--field <COL>")]
        [DefaultValue("ExpiryDate")]
        public string SortField { get; set; } = "ExpiryDate";

        [CommandOption("--dir <DIR>")]
        [DefaultValue("desc")]
        public string Direction { get; set; } = "desc";

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
            var data = await _api.ListClientRatesAsync(
                settings.ClientId, settings.EmpId, settings.ShowExpired,
                settings.PageSize, settings.Skip, settings.SortField, settings.Direction,
                selectAll: false, CancellationToken.None);
            if (data is null)
            {
                OutputHelper.WriteWarning("No rates returned.");
                return 0;
            }

            OutputHelper.Render(data, settings.Json, d =>
            {
                if (d.Rates.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No rates.[/]");
                    return;
                }
                var table = new Table().Expand();
                table.AddColumn("Employee");
                table.AddColumn("Client");
                table.AddColumn(new TableColumn("Rate").RightAligned());
                table.AddColumn(new TableColumn("Prepaid").RightAligned());
                table.AddColumn("Expires");
                table.AddColumn("Notes");

                foreach (var r in d.Rates)
                {
                    var expiry = r.ExpiryDate;
                    var expiryCell = expiry is null
                        ? "-"
                        : expiry.Value < DateTime.Today
                            ? $"[red]{expiry.Value:yyyy-MM-dd}[/]"
                            : (expiry.Value - DateTime.Today).Days <= 14
                                ? $"[yellow]{expiry.Value:yyyy-MM-dd}[/]"
                                : $"{expiry.Value:yyyy-MM-dd}";
                    table.AddRow(
                        Markup.Escape($"{r.EmpId} · {r.EmployeeName ?? "?"}"),
                        Markup.Escape($"{r.ClientId} · {r.ClientName ?? "?"}"),
                        $"${r.Rate:N2}",
                        r.PrepaidRate > 0 ? $"${r.PrepaidRate:N2}" : "-",
                        expiryCell,
                        Markup.Escape(r.Notes ?? "-"));
                }
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[dim]{d.Rates.Count} of {d.Total} rates[/]");
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
