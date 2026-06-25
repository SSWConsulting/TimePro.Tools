using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Rates;

[Description("Create a new client rate for the current employee")]
public class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--rate <RATE>")]
        [Description("Regular hourly rate. Defaults to the recommended rate (previous, else employee default)")]
        public decimal? Rate { get; set; }

        [CommandOption("--prepaid <RATE>")]
        [Description("Prepaid hourly rate. Defaults to the recommended prepaid rate")]
        public decimal? PrepaidRate { get; set; }

        [CommandOption("--expiry <DATE>")]
        [Description("Expiry date (yyyy-MM-dd). Defaults to 12 months from today")]
        public string? Expiry { get; set; }

        [CommandOption("--notes <NOTES>")]
        [Description("Optional notes")]
        public string? Notes { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public CreateCommand(ITimeProApiClient api, IConfigService config)
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
            // Fall back to the recommended rate when amounts aren't supplied.
            var rate = settings.Rate;
            var prepaid = settings.PrepaidRate;
            RateSource source = RateSource.None;
            if (rate is null || prepaid is null)
            {
                var init = await _api.InitializeClientRateAsync(tenant.EmployeeId, settings.ClientId, cancellationToken);
                if (init is not null)
                {
                    var rec = RateResolver.Recommend(init);
                    source = rec.Source;
                    rate ??= rec.Rate;
                    prepaid ??= rec.PrepaidRate;
                }
            }

            if (rate is null)
            {
                OutputHelper.WriteError("No rate supplied and none could be recommended. Pass --rate <N> (and optionally --prepaid <N>).");
                return 1;
            }
            prepaid ??= 0m;

            var expiry = settings.Expiry is not null
                ? DateOnly.ParseExact(settings.Expiry, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DateOnly.FromDateTime(DateTime.Today).AddMonths(12);

            var model = new SaveClientRateModel
            {
                ClientRateId = null, // create
                EmpId = tenant.EmployeeId,
                ClientId = settings.ClientId,
                Rate = rate,
                PrepaidRate = prepaid,
                ExpiryDate = expiry.ToDateTime(TimeOnly.MinValue),
                Notes = settings.Notes
            };

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine("[bold]Creating client rate:[/]");
                AnsiConsole.MarkupLine($"  Client:   {Markup.Escape(settings.ClientId)}");
                AnsiConsole.MarkupLine($"  Rate:     ${rate:F2}{(settings.Rate is null && source != RateSource.None ? $" [dim](recommended: {source})[/]" : "")}");
                AnsiConsole.MarkupLine($"  Prepaid:  ${prepaid:F2}");
                AnsiConsole.MarkupLine($"  Expiry:   {expiry:yyyy-MM-dd}");
                if (!string.IsNullOrEmpty(settings.Notes))
                    AnsiConsole.MarkupLine($"  Notes:    {Markup.Escape(settings.Notes)}");
                AnsiConsole.WriteLine();

                if (!settings.Yes && !AnsiConsole.Confirm("Create this rate?"))
                    return 1;
            }

            await _api.SaveClientRateAsync(model, cancellationToken);

            if (settings.Json)
                OutputHelper.WriteJson(new { created = true, clientId = settings.ClientId, rate, prepaidRate = prepaid, expiry = expiry.ToString("yyyy-MM-dd") });
            else
                OutputHelper.WriteSuccess($"Rate created for '{settings.ClientId}': ${rate:F2} (expires {expiry:yyyy-MM-dd})");

            return 0;
        }
        catch (ApiException ex)
        {
            var detail = ApiErrorParser.ExtractDetail(ex.ResponseBody);
            if (settings.Json)
                OutputHelper.WriteJsonError($"API error: {ex.Message}", ex.StatusCode, detail);
            else
            {
                OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
                if (detail is not null)
                    AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(detail)}[/]");
            }
            return 1;
        }
    }
}
