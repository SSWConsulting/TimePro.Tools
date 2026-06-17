using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Tenants;

[Description("Show active tenant details")]
public class TenantInfoCommand : Command<TenantInfoCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public TenantInfoCommand(IConfigService config)
    {
        _config = config;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant is null)
        {
            OutputHelper.WriteError("No active tenant. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        var summary = tenant.ToSummary();

        OutputHelper.Render(summary, settings.Json, t =>
        {
            var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
            table.AddRow("[bold]Tenant[/]", Markup.Escape(t.TenantId));
            table.AddRow("[bold]Employee[/]", Markup.Escape(t.EmployeeId ?? "unknown"));
            table.AddRow("[bold]Name[/]", Markup.Escape(t.EmployeeName ?? "unknown"));
            table.AddRow("[bold]API URL[/]", Markup.Escape(t.ApiUrl));
            table.AddRow("[bold]Environment[/]", t.IsProduction ? "[red]Production[/]" : "[green]Staging/Dev[/]");
            AnsiConsole.Write(table);
        });

        return 0;
    }
}
