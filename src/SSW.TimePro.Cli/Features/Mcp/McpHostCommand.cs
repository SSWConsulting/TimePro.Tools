using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Mcp;

[Description("Start MCP server (stdio transport)")]
public class McpHostCommand : AsyncCommand<McpHostCommand.Settings>
{
    public class Settings : CommandSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var builder = Host.CreateApplicationBuilder();

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
