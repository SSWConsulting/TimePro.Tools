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

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenants = _config.ListTenants();
        var global = _config.LoadGlobalConfig();

        if (tenants.Count == 0)
        {
            OutputHelper.WriteInfo("No tenants configured. Run 'tp login --tenant <id>' to add one.");
            return 0;
        }

        var activeFile = _config.LoadActiveTenantConfig()?.ConfigName ?? global.ActiveTenant;
        var items = tenants.Select(t => TenantListItem.From(t, activeFile)).ToList();

        OutputHelper.Render(items, settings.Json, list =>
        {
            var table = new Table()
                .AddColumn("")
                .AddColumn("File")
                .AddColumn("Tenant")
                .AddColumn("Employee")
                .AddColumn("Name")
                .AddColumn("Env")
                .AddColumn("API URL");

            foreach (var t in list)
            {
                table.AddRow(
                    t.IsActive ? "[green]*[/]" : " ",
                    Markup.Escape(t.File ?? "-"),
                    Markup.Escape(t.TenantId),
                    Markup.Escape(t.EmployeeId ?? "-"),
                    Markup.Escape(t.EmployeeName ?? "-"),
                    t.IsProduction ? "[red]prod[/]" : "[green]non-prod[/]",
                    Markup.Escape(t.ApiUrl));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[grey]* = active. Switch with 'tp tenant set <file>'.[/]");
        });

        return 0;
    }
}

public sealed record TenantListItem(
    string? File,
    bool IsActive,
    string TenantId,
    string ApiUrl,
    bool IsProduction,
    string? EmployeeId,
    string? EmployeeName,
    string AppName)
{
    public static TenantListItem From(TenantConfig tenant, string? activeTenantFile) =>
        new(
            File: tenant.ConfigName,
            IsActive: tenant.ConfigName is not null
                && string.Equals(tenant.ConfigName, activeTenantFile, StringComparison.OrdinalIgnoreCase),
            TenantId: tenant.TenantId,
            ApiUrl: tenant.ApiUrl,
            IsProduction: tenant.IsProduction,
            EmployeeId: tenant.EmployeeId,
            EmployeeName: tenant.EmployeeName,
            AppName: tenant.AppName);
}
