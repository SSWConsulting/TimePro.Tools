using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Tenants;

[Description("Switch the active tenant")]
public class TenantSetCommand : Command<TenantSetCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<TENANT_ID>")]
        [Description("Tenant ID to activate")]
        public string TenantId { get; set; } = string.Empty;
    }

    public TenantSetCommand(IConfigService config)
    {
        _config = config;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var tenant = _config.LoadTenantConfig(settings.TenantId);
        if (tenant is null)
        {
            OutputHelper.WriteError($"Tenant '{settings.TenantId}' not found. Run 'tp login --tenant {settings.TenantId}' first.");
            return 1;
        }

        var global = _config.LoadGlobalConfig();
        global.ActiveTenant = settings.TenantId;
        _config.SaveGlobalConfig(global);

        OutputHelper.WriteSuccess($"Active tenant set to '{settings.TenantId}' ({tenant.EmployeeName ?? tenant.EmployeeId ?? "unknown"})");
        return 0;
    }
}
