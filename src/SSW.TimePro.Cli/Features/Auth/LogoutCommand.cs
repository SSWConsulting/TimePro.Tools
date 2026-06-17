using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Auth;

[Description("Remove stored credentials for a tenant")]
public class LogoutCommand : AsyncCommand<LogoutCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--tenant <TENANT>")]
        [Description("Tenant ID to remove (default: active tenant)")]
        public string? Tenant { get; set; }
    }

    public LogoutCommand(IConfigService config)
    {
        _config = config;
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var global = _config.LoadGlobalConfig();
        var tenantId = settings.Tenant ?? global.ActiveTenant;

        if (string.IsNullOrEmpty(tenantId))
        {
            OutputHelper.WriteError("No active tenant. Use --tenant to specify.");
            return Task.FromResult(1);
        }

        _config.DeleteTenantConfig(tenantId);

        if (global.ActiveTenant == tenantId)
        {
            global.ActiveTenant = null;
            _config.SaveGlobalConfig(global);
        }

        OutputHelper.WriteSuccess($"Logged out from tenant '{tenantId}'");
        return Task.FromResult(0);
    }
}
