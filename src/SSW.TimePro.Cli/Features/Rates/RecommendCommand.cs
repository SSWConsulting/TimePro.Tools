using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Rates;

[Description("Show the recommended rate for a client (latest client rate, else employee default)")]
public class RecommendCommand : AsyncCommand<RecommendCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public RecommendCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.ClientId))
        {
            OutputHelper.WriteError("--client is required");
            return 1;
        }

        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var init = await _api.InitializeClientRateAsync(tenant.EmployeeId, settings.ClientId, cancellationToken);
            if (init is null)
            {
                if (settings.Json)
                    OutputHelper.WriteJson(new { found = false, clientId = settings.ClientId });
                else
                    OutputHelper.WriteWarning($"No rate information available for client '{settings.ClientId}'.");
                return 0;
            }

            var rec = RateResolver.Recommend(init);

            if (settings.Json)
            {
                OutputHelper.WriteJson(new
                {
                    clientId = settings.ClientId,
                    init.PreviousRate,
                    init.PreviousPrepaidRate,
                    init.DefaultRate,
                    init.DefaultPrepaidRate,
                    recommended = new { rec.Rate, rec.PrepaidRate, source = rec.Source.ToString() }
                });
                return 0;
            }

            var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
            table.AddRow("[bold]Client[/]", Markup.Escape(init.Client ?? settings.ClientId));
            table.AddRow("[bold]Previous rate[/]", $"${init.PreviousRate ?? 0m:F2} / prepaid ${init.PreviousPrepaidRate ?? 0m:F2}");
            table.AddRow("[bold]Employee default[/]", $"${init.DefaultRate ?? 0m:F2} / prepaid ${init.DefaultPrepaidRate ?? 0m:F2}");
            table.AddRow("[bold]Recommended[/]", $"[green]${rec.Rate:F2}[/] / prepaid ${rec.PrepaidRate:F2} [dim]({rec.Source})[/]");
            AnsiConsole.Write(table);

            if (rec.Source == RateSource.None)
                OutputHelper.WriteWarning("No previous or default rate to recommend — set one with 'tp rate create --client <ID> --rate <N>'.");

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
