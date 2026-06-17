using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Location;

[Description("Show location defaults and WFH days")]
public class InfoCommand : AsyncCommand<InfoCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--date <DATE>")]
        [Description("Check location for a specific date")]
        public string? Date { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public InfoCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var global = _config.LoadGlobalConfig();

        if (settings.Date is not null)
        {
            var date = DateOnly.ParseExact(settings.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dayName = date.DayOfWeek.ToString();
            var isWfh = global.WfhDays.Contains(dayName, StringComparer.OrdinalIgnoreCase);
            var location = isWfh ? "Home" : global.DefaultLocation;

            OutputHelper.Render(new { date = settings.Date, dayOfWeek = dayName, location, isWfh }, settings.Json, _ =>
            {
                AnsiConsole.MarkupLine($"[bold]{date:dddd, MMM d yyyy}[/]: {Markup.Escape(location)}{(isWfh ? " [dim](WFH day)[/]" : "")}");
            });
            return 0;
        }

        // Fetch valid locations from the API
        var apiLocations = await _api.GetLocationsAsync(CancellationToken.None);

        var data = new
        {
            defaultLocation = global.DefaultLocation,
            wfhDays = global.WfhDays,
            availableLocations = apiLocations.Select(l => new { l.LocationId, l.LocationName })
        };

        OutputHelper.Render(data, settings.Json, _ =>
        {
            AnsiConsole.MarkupLine($"[bold]Default location:[/] {Markup.Escape(global.DefaultLocation)}");
            if (global.WfhDays.Count > 0)
                AnsiConsole.MarkupLine($"[bold]WFH days:[/]         {string.Join(", ", global.WfhDays)}");
            else
                AnsiConsole.MarkupLine("[bold]WFH days:[/]         [dim]None set[/]");

            if (apiLocations.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Valid locations:[/]");
                foreach (var loc in apiLocations)
                    AnsiConsole.MarkupLine($"  {Markup.Escape(loc.LocationId ?? "?"),-10} {Markup.Escape(loc.LocationName ?? "")}");
            }
        });

        return 0;
    }
}
