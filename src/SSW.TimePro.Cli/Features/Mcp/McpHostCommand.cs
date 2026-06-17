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
    public class Settings : CommandSettings { }

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
        await app.RunAsync();

        return 0;
    }
}
