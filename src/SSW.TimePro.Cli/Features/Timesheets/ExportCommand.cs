using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Export timesheets to CSV")]
public class ExportCommand : AsyncCommand<ExportCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandOption("--from <DATE>")]
        [Description("Start date (yyyy-MM-dd). Defaults to 3 months ago")]
        public string? From { get; set; }

        [CommandOption("--to <DATE>")]
        [Description("End date (yyyy-MM-dd). Defaults to today")]
        public string? To { get; set; }

        [CommandOption("--output <FILE>")]
        [Description("Output file path. Defaults to timesheets-export.csv")]
        public string Output { get; set; } = "timesheets-export.csv";
    }

    public ExportCommand(ITimeProApiClient api) => _api = api;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var from = settings.From is not null
            ? DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Today.AddMonths(-3));

        var to = settings.To is not null
            ? DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Today);

        try
        {
            var csvBytes = await _api.ExportTimesheetsCsvAsync(from, to, CancellationToken.None);
            await File.WriteAllBytesAsync(settings.Output, csvBytes);

            OutputHelper.WriteSuccess($"Exported to {settings.Output} ({csvBytes.Length:N0} bytes)");
            OutputHelper.WriteInfo($"Date range: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
