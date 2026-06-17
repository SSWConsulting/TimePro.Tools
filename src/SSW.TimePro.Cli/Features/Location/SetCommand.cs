using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Location;

[Description("Set WFH day defaults")]
public class SetCommand : Command<SetCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<LOCATION>")]
        [Description("Location: SSW, Home, Client, Travel, Other (or aliases like Office, WFH)")]
        public string Location { get; set; } = string.Empty;

        [CommandOption("--day <DAYS>")]
        [Description("Comma-separated days (e.g., Mon,Tue,Wed)")]
        public string? Days { get; set; }
    }

    public SetCommand(IConfigService config) => _config = config;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var global = _config.LoadGlobalConfig();
        var resolvedLocation = LocationResolver.Resolve(settings.Location);

        if (settings.Days is not null)
        {
            // Setting WFH for specific days
            var dayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mon"] = "Monday", ["tue"] = "Tuesday", ["wed"] = "Wednesday",
                ["thu"] = "Thursday", ["fri"] = "Friday",
                ["monday"] = "Monday", ["tuesday"] = "Tuesday", ["wednesday"] = "Wednesday",
                ["thursday"] = "Thursday", ["friday"] = "Friday"
            };

            var days = new List<string>();
            foreach (var raw in settings.Days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (dayMap.TryGetValue(raw, out var full))
                    days.Add(full);
                else
                {
                    OutputHelper.WriteError($"Unknown day: '{raw}'. Use Mon,Tue,Wed,Thu,Fri.");
                    return 1;
                }
            }

            if (resolvedLocation == "Home")
            {
                global.WfhDays = days;
            }
            else
            {
                // Setting non-home days = remove those from WFH
                global.WfhDays = global.WfhDays
                    .Where(d => !days.Contains(d, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
        }
        else
        {
            // Setting default location (stored as canonical API ID)
            global.DefaultLocation = resolvedLocation;
        }

        _config.SaveGlobalConfig(global);
        OutputHelper.WriteSuccess($"Location defaults updated");

        if (global.WfhDays.Count > 0)
            OutputHelper.WriteInfo($"WFH days: {string.Join(", ", global.WfhDays)}");

        OutputHelper.WriteInfo($"Default location: {global.DefaultLocation}");

        return 0;
    }
}
