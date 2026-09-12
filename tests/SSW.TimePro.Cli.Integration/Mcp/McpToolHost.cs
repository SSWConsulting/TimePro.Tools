using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Features.Mcp.Tools;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// The four MCP tool classes wired against a test API client, so a case catalog can invoke any
/// tool without repeating the dependency graph.
/// </summary>
public sealed class McpToolHost
{
    public McpToolHost(ITimeProApiClient api, IConfigService config)
    {
        var updates = new TimesheetUpdateService(api);
        var creates = new TimesheetCreateService(api, config);

        Timesheets = new TimesheetMcpTools(api, config, creates, updates, new TimesheetAcceptService(api, updates));
        Lookups = new LookupMcpTools(api, config);
        Leave = new LeaveMcpTools(
            api,
            config,
            new LeaveCreateService(api),
            new LeaveUpdateService(api, new LeaveLookup(api)),
            new LeaveListService(api));
        Accounting = new AccountingMcpTools(api, config, new LeaveBalanceImportService(api));
    }

    public TimesheetMcpTools Timesheets { get; }
    public LookupMcpTools Lookups { get; }
    public LeaveMcpTools Leave { get; }
    public AccountingMcpTools Accounting { get; }
}
