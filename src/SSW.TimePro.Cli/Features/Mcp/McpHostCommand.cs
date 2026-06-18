using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
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
        [Description("Bind this MCP session to a specific tenant config (by name, e.g. ssw-staging). Defaults to the active tenant; does NOT change the global active tenant.")]
        public string? Tenant { get; set; }
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
        builder.Services.AddHttpClient<ITimeProApiClient, TimeProApiClient>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var app = builder.Build();

        // Optional per-session tenant override — binds this MCP process to a specific
        // tenant config without touching the global active tenant (config.json).
        if (!string.IsNullOrWhiteSpace(settings.Tenant))
        {
            var config = app.Services.GetRequiredService<IConfigService>();
            var tenant = config.LoadTenantConfig(settings.Tenant.Trim());
            if (tenant is null)
            {
                await Console.Error.WriteLineAsync(
                    $"Unknown tenant '{settings.Tenant}'. Add it with 'tp login --tenant {settings.Tenant}' or pick one from 'tp tenant list'.");
                return 1;
            }
            if (app.Services.GetRequiredService<ITenantProvider>() is DefaultTenantProvider provider)
                provider.SetOverride(tenant);
        }

        await app.RunAsync();

        return 0;
    }
}
