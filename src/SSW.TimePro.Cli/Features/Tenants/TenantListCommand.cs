using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Tenants;

[Description("List all stored tenants")]
public class TenantListCommand : Command<TenantListCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public TenantListCommand(IConfigService config)
    {
        _config = config;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var tenants = _config.ListTenants();
        var global = _config.LoadGlobalConfig();

        if (tenants.Count == 0)
        {
            OutputHelper.WriteInfo("No tenants configured. Run 'tp login --tenant <id>' to add one.");
            return 0;
        }

        var summaries = tenants.Select(t => t.ToSummary()).ToList();

        OutputHelper.Render(summaries, settings.Json, list =>
        {
            var table = new Table()
                .AddColumn("")
                .AddColumn("Tenant")
                .AddColumn("Employee")
                .AddColumn("Name")
                .AddColumn("API URL");

            foreach (var t in list)
            {
                var active = t.TenantId == global.ActiveTenant ? "[green]*[/]" : " ";
                table.AddRow(
                    active,
                    Markup.Escape(t.TenantId),
                    Markup.Escape(t.EmployeeId ?? "-"),
                    Markup.Escape(t.EmployeeName ?? "-"),
                    Markup.Escape(t.ApiUrl));
            }

            AnsiConsole.Write(table);
        });

        return 0;
    }
}
