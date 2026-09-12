using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Telemetry;

namespace SSW.TimePro.Cli.Features.Mcp;

/// <summary>
/// Sets the client context around every MCP tool call and logs the call locally. Overlapping calls
/// stay isolated because the context is AsyncLocal and set inside this per-call scope.
/// </summary>
public static class McpInvocationFilter
{
    public static IMcpServerBuilder WithClientContext(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, ct) =>
        {
            var invocation = ClientContext.BeginMcpTool(context.Params?.Name);
            var started = Stopwatch.StartNew();
            string? failure = null;

            try
            {
                return await next(context, ct);
            }
            catch
            {
                failure = "error";
                throw;
            }
            finally
            {
                started.Stop();
                Record(context, invocation, started.ElapsedMilliseconds, failure);
                ClientContext.Clear();
            }
        }));

    private static void Record(
        RequestContext<CallToolRequestParams> context,
        ClientInvocation invocation,
        long durationMs,
        string? failure)
    {
        var config = context.Services?.GetService<IConfigService>();
        CommandLog.Record(
            invocation,
            () => config?.LoadGlobalConfig(),
            () => config?.LoadActiveTenantConfig(),
            durationMs,
            exitCode: null,
            failure);
    }
}
