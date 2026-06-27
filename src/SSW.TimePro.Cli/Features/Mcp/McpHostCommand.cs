using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Features.Accounting;
using SSW.TimePro.Cli.Features.Mcp.Tools;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Mcp;

[Description("Start MCP server (stdio transport)")]
public class McpHostCommand : AsyncCommand<McpHostCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--tenant <NAME>")]
        [Description("Bind this MCP session to a specific tenant config (by name, e.g. northwind-staging). Defaults to the active tenant; does NOT change the global active tenant.")]
        public string? Tenant { get; set; }

        [CommandOption("--env|--environment <NAME>")]
        [Description("Bind this MCP session to a tenant environment config, e.g. staging resolves active tenant 'northwind' to 'northwind-staging'. Does NOT change the global active tenant.")]
        public string? Environment { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        // stdio MCP transport: stdout must carry only JSON-RPC frames.
        // Route all console logging to stderr so host/logger output doesn't corrupt the stream.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton<IConfigService, ConfigService>();
        builder.Services.AddSingleton<ITenantProvider, DefaultTenantProvider>();
        builder.Services.AddTransient<IAccountingDiagnosticsService, AccountingDiagnosticsService>();
        builder.Services.AddHttpClient<ITimeProApiClient, TimeProApiClient>();

        var mcpServer = builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<TimesheetMcpTools>()
            .WithTools<LookupMcpTools>()
            .WithTools<LeaveMcpTools>();

        var globalConfig = new ConfigService().LoadGlobalConfig();

        if (globalConfig.IsFeatureEnabled(FeatureCatalog.Accounting))
            mcpServer.WithTools<AccountingMcpTools>();

        var app = builder.Build();

        var config = app.Services.GetRequiredService<IConfigService>();

        // Optional per-session tenant/environment override — binds this MCP process
        // to a specific tenant config without touching the global active tenant.
        var tenantOverride = TenantOverrideResolver.ResolveTenantOverride(
            config,
            new TenantOverrideOptions(settings.Tenant, settings.Environment),
            out var tenantOverrideError);

        if (tenantOverrideError is not null)
        {
            await Console.Error.WriteLineAsync(tenantOverrideError);
            return 1;
        }

        if (tenantOverride is not null)
        {
            config.SetActiveTenantOverride(tenantOverride);
        }
        else
        {
            // Single-tenant convenience (MCP host only): when no --tenant was given AND no
            // active tenant is set BUT exactly one tenant config exists, bind to it so a
            // fresh single-tenant install works in MCP without running 'tp tenant set'.
            // Does NOT touch the global active tenant. Zero/multiple configs → unchanged
            // (tools surface "Not logged in").
            if (config.LoadActiveTenantConfig() is null)
            {
                var tenants = config.ListTenants();
                if (tenants.Count == 1)
                    config.SetActiveTenantOverride(tenants[0]);
            }
        }

        await app.RunAsync();

        return 0;
    }
}
