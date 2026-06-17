using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Clients;

[Description("Search for clients by name")]
public class SearchCommand : AsyncCommand<SearchCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<QUERY>")]
        [Description("Search text")]
        public string Query { get; set; } = string.Empty;

        [CommandOption("--limit <N>")]
        [Description("Max results (default: 20)")]
        public int Limit { get; set; } = 20;

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public SearchCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var results = await _api.SearchClientsAsync(tenant.EmployeeId, settings.Query, CancellationToken.None);
            var limited = results.Take(settings.Limit).ToList();

            OutputHelper.Render(limited, settings.Json, list =>
            {
                if (list.Count == 0)
                {
                    OutputHelper.WriteInfo($"No clients matching '{settings.Query}'");
                    return;
                }

                var table = new Table().AddColumn("ID").AddColumn("Name");
                foreach (var c in list)
                    table.AddRow(
                        Markup.Escape(c.Value ?? ""),
                        Markup.Escape(c.Text ?? ""));
                AnsiConsole.Write(table);
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
