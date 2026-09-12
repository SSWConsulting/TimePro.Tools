using WireMock.Server;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// One golden case: how to invoke a tool, what to change about the shared Northwind API state
/// first, and which snapshot the raw returned JSON must match.
/// </summary>
public sealed record McpToolCase(
    string ToolName,
    string CaseName,
    Func<McpToolHost, CancellationToken, Task<string>> Invoke)
{
    /// <summary>Route this tool reads or writes first; drives the generated empty/error cases.</summary>
    public string? PrimaryRoute { get; init; }

    public string PrimaryMethod { get; init; } = "GET";

    /// <summary>When set, an <c>empty</c> case replaces the primary route with this body.</summary>
    public string? EmptyBody { get; init; }

    /// <summary>Set false for tools whose primary call cannot fail on its own (pure local reads).</summary>
    public bool HasApiErrorCase { get; init; } = true;

    /// <summary>Extra WireMock state this case needs on top of the Northwind baseline.</summary>
    public Action<WireMockServer>? Arrange { get; init; }

    /// <summary>
    /// Replaces the checked week's Mon–Fri dates with tokens. Only for tools whose window is
    /// derived from "today" and therefore cannot be pinned without a production clock seam.
    /// </summary>
    public bool TokenizeCurrentWeek { get; init; }

    public string GoldenPath => $"Tools/{ToolName}.{CaseName}.json";

    public string DisplayName => $"{ToolName}/{CaseName}";

    public override string ToString() => DisplayName;
}
