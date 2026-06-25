using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Rates;

[Description("Update an existing client rate for the current employee")]
public class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--client <CLIENT_ID>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--id <RATE_ID>")]
        [Description("ClientRateId to update. Defaults to the current active rate")]
        public int? RateId { get; set; }

        [CommandOption("--rate <RATE>")]
        [Description("New regular hourly rate (unchanged if omitted)")]
        public decimal? Rate { get; set; }

        [CommandOption("--prepaid <RATE>")]
        [Description("New prepaid hourly rate (unchanged if omitted)")]
        public decimal? PrepaidRate { get; set; }

        [CommandOption("--expiry <DATE>")]
        [Description("New expiry date (yyyy-MM-dd, unchanged if omitted)")]
        public string? Expiry { get; set; }

        [CommandOption("--notes <NOTES>")]
        [Description("New notes (unchanged if omitted)")]
        public string? Notes { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public UpdateCommand(ITimeProApiClient api, IConfigService config)
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
            // Resolve the rate to update: an explicit --id (any rate, incl. expired) or the current active one.
            int? rateId = settings.RateId;
            decimal? existingRate = null;
            decimal? existingPrepaid = null;
            DateOnly? existingExpiry = null;
            string? existingNotes = null;

            if (rateId is not null)
            {
                var table = await _api.ListClientRatesAsync(
                    settings.ClientId, tenant.EmployeeId, showExpired: true,
                    pageSize: 100, skip: 0, sortField: "ExpiryDate", direction: "desc", selectAll: false, cancellationToken);
                var row = table?.Rates.FirstOrDefault(r => r.ClientRateId == rateId);
                if (row is null)
                {
                    OutputHelper.WriteError($"No rate with id {rateId} found for client '{settings.ClientId}'.");
                    return 1;
                }
                existingRate = row.Rate;
                existingPrepaid = row.PrepaidRate;
                existingExpiry = row.ExpiryDate is { } e ? DateOnly.FromDateTime(e) : null;
                existingNotes = row.Notes;
            }
            else
            {
                var current = await _api.GetClientRateAsync(
                    tenant.EmployeeId, settings.ClientId, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
                if (current?.ClientRateId is null)
                {
                    OutputHelper.WriteError($"No active rate to update for client '{settings.ClientId}'.");
                    OutputHelper.WriteInfo("Pass --id <RATE_ID> (see 'tp rate list --client <ID>') or create one with 'tp rate create'.");
                    return 1;
                }
                rateId = current.ClientRateId;
                existingRate = current.Rate;
                existingPrepaid = current.PrepaidRate;
                existingExpiry = !string.IsNullOrEmpty(current.ExpiryDate) ? DateOnly.FromDateTime(DateTime.Parse(current.ExpiryDate)) : null;
                existingNotes = current.Notes;
            }

            var rate = settings.Rate ?? existingRate;
            var prepaid = settings.PrepaidRate ?? existingPrepaid ?? 0m;
            var expiry = settings.Expiry is not null
                ? DateOnly.ParseExact(settings.Expiry, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : existingExpiry;
            var notes = settings.Notes ?? existingNotes;

            if (rate is null)
            {
                OutputHelper.WriteError("No rate value resolved; pass --rate <N>.");
                return 1;
            }

            var model = new SaveClientRateModel
            {
                ClientRateId = rateId,
                EmpId = tenant.EmployeeId,
                ClientId = settings.ClientId,
                Rate = rate,
                PrepaidRate = prepaid,
                ExpiryDate = expiry?.ToDateTime(TimeOnly.MinValue),
                Notes = notes
            };

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine("[bold]Updating client rate:[/]");
                AnsiConsole.MarkupLine($"  Client:   {Markup.Escape(settings.ClientId)} (rate #{rateId})");
                AnsiConsole.MarkupLine($"  Rate:     ${rate:F2}");
                AnsiConsole.MarkupLine($"  Prepaid:  ${prepaid:F2}");
                AnsiConsole.MarkupLine($"  Expiry:   {(expiry is { } x ? x.ToString("yyyy-MM-dd") : "(none)")}");
                if (!string.IsNullOrEmpty(notes))
                    AnsiConsole.MarkupLine($"  Notes:    {Markup.Escape(notes)}");
                AnsiConsole.WriteLine();

                if (!settings.Yes && !AnsiConsole.Confirm("Update this rate?"))
                    return 1;
            }

            await _api.SaveClientRateAsync(model, cancellationToken);

            if (settings.Json)
                OutputHelper.WriteJson(new { updated = true, clientRateId = rateId, clientId = settings.ClientId, rate, prepaidRate = prepaid, expiry = expiry?.ToString("yyyy-MM-dd") });
            else
                OutputHelper.WriteSuccess($"Rate #{rateId} updated for '{settings.ClientId}': ${rate:F2}{(expiry is { } y ? $" (expires {y:yyyy-MM-dd})" : "")}");

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
