using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Projects;

[Description("List projects for a client")]
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
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ListCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
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
            var projects = await _api.GetProjectsForClientAsync(
                tenant.EmployeeId, settings.ClientId, CancellationToken.None);

            OutputHelper.Render(projects, settings.Json, list =>
            {
                if (list.Count == 0)
                {
                    OutputHelper.WriteInfo($"No projects for client '{settings.ClientId}'");
                    return;
                }

                var table = new Table()
                    .AddColumn("ID")
                    .AddColumn("Project")
                    .AddColumn("Iterations")
                    .AddColumn("Type");

                foreach (var p in list)
                {
                    var type = p.IsLeave ? "[yellow]Leave[/]" : p.IsGeneral ? "[dim]General[/]" : "";
                    table.AddRow(
                        Markup.Escape(p.Value ?? ""),
                        Markup.Escape(p.DisplayText ?? ""),
                        p.UseIteration ? "Yes" : "[dim]No[/]",
                        type);
                }

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
