using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Rates;

[Description("Get client rate for the current employee")]
public class GetCommand : AsyncCommand<GetCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--date <DATE>")]
        [Description("Date for rate lookup (default: today)")]
        public string? Date { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public GetCommand(ITimeProApiClient api, IConfigService config)
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

        var date = RateLookup.ResolveDate(settings.Date);

        try
        {
            var rate = await RateLookup.FetchAsync(
                _api, tenant.EmployeeId, settings.ClientId, date, CancellationToken.None);

            // A missing rate is a valid lookup result, not a failure, so it shares the hit schema.
            if (settings.Json)
            {
                OutputHelper.WriteJson(RateLookupResult.From(settings.ClientId, date, rate));
                WarnAboutExpiry(rate);
                return 0;
            }

            if (rate is null)
            {
                OutputHelper.WriteWarning($"No rate found for client '{settings.ClientId}' on {date:yyyy-MM-dd}");
                return 0;
            }

            RenderRateTable(rate);
            WarnAboutExpiry(rate);
            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteApiError(ex, settings.Json);
            return 1;
        }
    }

    private static void RenderRateTable(ClientRateResponse r)
    {
        var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
        table.AddRow("[bold]Client[/]", Markup.Escape($"{r.ClientName} ({r.ClientId})"));
        table.AddRow("[bold]Employee[/]", Markup.Escape($"{r.EmployeeName} ({r.EmpId})"));
        table.AddRow("[bold]Rate[/]", $"${r.Rate:F2}");
        if (r.PrepaidRate.HasValue && r.PrepaidRate > 0)
            table.AddRow("[bold]Prepaid Rate[/]", $"${r.PrepaidRate:F2}");

        if (!string.IsNullOrEmpty(r.ExpiryDate))
        {
            var expiry = DateTime.Parse(r.ExpiryDate);
            var daysUntilExpiry = (expiry - DateTime.Today).Days;

            if (daysUntilExpiry < 0)
                table.AddRow("[bold]Expiry[/]", $"[red]{r.ExpiryDate} (EXPIRED {Math.Abs(daysUntilExpiry)} days ago)[/]");
            else if (daysUntilExpiry <= 7)
                table.AddRow("[bold]Expiry[/]", $"[yellow]{r.ExpiryDate} (expires in {daysUntilExpiry} days)[/]");
            else
                table.AddRow("[bold]Expiry[/]", Markup.Escape(r.ExpiryDate));
        }

        if (!string.IsNullOrEmpty(r.Notes))
            table.AddRow("[bold]Notes[/]", Markup.Escape(r.Notes));

        AnsiConsole.Write(table);
    }

    private static void WarnAboutExpiry(ClientRateResponse? rate)
    {
        if (string.IsNullOrEmpty(rate?.ExpiryDate))
            return;

        var expiry = DateTime.Parse(rate.ExpiryDate);
        if (expiry < DateTime.Today)
            OutputHelper.WriteWarning("This rate has expired. Contact your admin to renew it.");
        else if ((expiry - DateTime.Today).Days <= 7)
            OutputHelper.WriteWarning("This rate expires soon. Consider renewing it.");
    }
}
