namespace SSW.TimePro.Cli.Infrastructure.Telemetry;

public static class ClientSurface
{
    public const string Cli = "cli";
    public const string Mcp = "mcp";
}

public sealed record RequestRecord(string RequestId, string Method, string Route, int? Status);

/// <summary>
/// One CLI invocation or one MCP tool call, and the HTTP attempts it made.
/// </summary>
public sealed class ClientInvocation
{
    private readonly List<RequestRecord> _requests = [];
    private readonly Lock _gate = new();

    public ClientInvocation(string surface, string command)
    {
        Surface = surface;
        Command = command;
    }

    public string Surface { get; }

    /// <summary>Canonical command path (<c>ts update</c>) or <c>mcp:&lt;ToolName&gt;</c>.</summary>
    public string Command { get; }

    public IReadOnlyList<RequestRecord> Requests
    {
        get { lock (_gate) return _requests.ToArray(); }
    }

    public void Record(RequestRecord request)
    {
        lock (_gate) _requests.Add(request);
    }
}

/// <summary>
/// Ambient command context for outgoing API calls. AsyncLocal rather than a static field because
/// MCP tool calls can overlap in one process and must not see each other's command name.
/// </summary>
public static class ClientContext
{
    private static readonly AsyncLocal<ClientInvocation?> CurrentInvocation = new();

    public static ClientInvocation? Current => CurrentInvocation.Value;

    /// <summary>Whether <c>--verbose</c> was requested. Process-wide: only the CLI surface sets it.</summary>
    public static bool Verbose { get; set; }

    public static ClientInvocation BeginCli(string command) => Begin(ClientSurface.Cli, command);

    public static ClientInvocation BeginMcpTool(string toolName) =>
        Begin(ClientSurface.Mcp, $"mcp:{toolName}");

    private static ClientInvocation Begin(string surface, string command)
    {
        var invocation = new ClientInvocation(surface, command);
        CurrentInvocation.Value = invocation;
        return invocation;
    }

    public static void Clear() => CurrentInvocation.Value = null;
}
