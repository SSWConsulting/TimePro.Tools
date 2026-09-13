using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Byte-for-byte SaveTimesheet payload equality between <c>ts create</c> and the
/// <c>CreateTimesheet</c> tool. Equal results would not prove equal writes, so this compares the
/// request bodies and the traffic that produced them.
/// </summary>
[Collection(CliConsoleCollection.Name)]
public class CliMcpCreatePayloadTests : TestBase
{
    private const string Client = NorthwindApi.ClientId;
    private const string Project = NorthwindApi.ProjectId;
    private const string Date = NorthwindApi.AnyDate;
    private const string SaveRoute = "/api/Timesheets/SaveTimesheet";
    private const string RateRoute = "/api/Timesheets/GetClientRate";
    private const string SaveRateRoute = "/api/Timesheets/SaveClientRate";
    private const string SummaryRoute = "/api/timesheetSummary/GetTableSummarydata";
    private const string ListRoute = "/api/Timesheets/GetTimesheetListViewModel";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("B", 175)]
    [InlineData("BPP", 150)]
    [InlineData("W", 175)]
    public async Task Create_PricesEachBillableTypeFromTheClientRate(string billable, int sellPrice)
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        var cli = await CliPayloadAsync(config, CreateArgs("--billable", billable));
        var mcp = await McpPayloadAsync(config, h => h.Timesheets.CreateTimesheet(
            Client, Project, Date, description: "Product search", billableId: billable,
            iterationId: 3402, ct: Ct));

        mcp.Should().Be(cli);
        Field(cli, "sellPrice").GetDecimal().Should().Be(sellPrice);
        Field(cli, "iterationID").GetInt32().Should().Be(3402);
    }

    [Fact]
    public async Task Create_TakesTheCategoryFromTheRepoMappingBeforeRecentEntries()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        var cli = await CliPayloadAsync(config, CreateArgs());
        var cliRequests = Paths();
        var mcp = await McpPayloadAsync(config, Invoke());

        mcp.Should().Be(cli);
        Field(cli, "categoryID").GetString().Should().Be("WEBDEV");
        cliRequests.Should().NotContain(SummaryRoute);
        Paths().Should().NotContain(SummaryRoute);
    }

    [Fact]
    public async Task Create_FallsBackToTheCategoryOnRecentEntries()
    {
        var config = new TestConfigService(McpToolCatalog.Tenant(WireMock.Url!));

        var cli = await CliPayloadAsync(config, CreateArgs(), RecentCategory("TRAIN"));
        var mcp = await McpPayloadAsync(config, Invoke(), RecentCategory("TRAIN"));

        mcp.Should().Be(cli);
        Field(cli, "categoryID").GetString().Should().Be("TRAIN");
        Paths().Should().Contain(SummaryRoute);
    }

    [Fact]
    public async Task Create_PrefersTheExplicitCategory()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        var cli = await CliPayloadAsync(config, CreateArgs("--category", "TRAIN"));
        var mcp = await McpPayloadAsync(config, h => h.Timesheets.CreateTimesheet(
            Client, Project, Date, description: "Product search", categoryId: "TRAIN",
            iterationId: 3402, ct: Ct));

        mcp.Should().Be(cli);
        Field(cli, "categoryID").GetString().Should().Be("TRAIN");
    }

    [Fact]
    public async Task Create_ResolvesTheExplicitLocationAlias()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        var cli = await CliPayloadAsync(config, CreateArgs("--location", "At Home"));
        var mcp = await McpPayloadAsync(config, h => h.Timesheets.CreateTimesheet(
            Client, Project, Date, description: "Product search", location: "At Home",
            iterationId: 3402, ct: Ct));

        mcp.Should().Be(cli);
        Field(cli, "locationID").GetString().Should().Be("Home");
    }

    [Fact]
    public async Task Create_UsesTheWfhDefaultForTheDayWhenNoLocationIsGiven()
    {
        var config = new TestConfigService(McpToolCatalog.Tenant(WireMock.Url!))
        {
            Global = new GlobalConfig { DefaultLocation = "Office", WfhDays = ["Monday"] }
        };

        var cli = await CliPayloadAsync(config, CreateArgs());
        var mcp = await McpPayloadAsync(config, Invoke());

        mcp.Should().Be(cli);
        Field(cli, "locationID").GetString().Should().Be("Home");
    }

    /// <summary>
    /// The MCP tool has no deducted-minutes argument, so this dimension is CLI-only until the
    /// tool schema changes.
    /// </summary>
    [Fact]
    public async Task Create_SendsDeductedMinutesAsHours()
    {
        var cli = await CliPayloadAsync(McpToolCatalog.Config(WireMock.Url!), CreateArgs("--less", "90"));

        Field(cli, "timeLess").GetDecimal().Should().Be(1.5m);
    }

    [Fact]
    public async Task Create_WhenTheRateHasExpired_WritesNothingOnEitherSurface()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        NorthwindApi.StubAll(WireMock);
        ExpiredRate(WireMock);
        var cli = await CliRunner.RunAsync(
            [.. CreateArgs(), "--reject-if-rate-expired"], ApiClient, config, Ct);

        cli.ExitCode.Should().Be(1);
        AssertNoWrites();

        WireMock.Reset();
        NorthwindApi.StubAll(WireMock);
        ExpiredRate(WireMock);
        var mcp = await new McpToolHost(ApiClient, config).Timesheets.CreateTimesheet(
            Client, Project, Date, description: "Product search", iterationId: 3402, ct: Ct);

        AssertNoWrites();
        using var doc = JsonDocument.Parse(mcp);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("No active rate");
        doc.RootElement.GetProperty("recovery").GetProperty("reason").GetString()
            .Should().Be("no_active_rate");
        doc.RootElement.GetProperty("recovery").GetProperty("steps")[0].GetProperty("command")
            .GetString().Should().StartWith($"tp rate create --client {Client}");
    }

    [Fact]
    public async Task Create_WhenTheDateIsMalformed_FailsWithTheErrorShapeAndWritesNothing()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        Arrange(null);
        var cli = await CliRunner.RunAsync(
            ["ts", "create", "--client", Client, "--project", Project, "--date", "16/03/2026",
             "--yes", "--json"], ApiClient, config, Ct);

        cli.ExitCode.Should().Be(1);
        using var cliDoc = JsonDocument.Parse(cli.Stdout);
        cliDoc.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("Use yyyy-MM-dd");
        AssertNoWrites();

        var mcp = await RunToolAsync(config, h => h.Timesheets.CreateTimesheet(
            Client, Project, "16/03/2026", ct: Ct));

        AssertNoWrites();
        using var mcpDoc = JsonDocument.Parse(mcp);
        mcpDoc.RootElement.GetProperty("error").GetString().Should().Contain("Use yyyy-MM-dd");
    }

    [Fact]
    public async Task Create_WhenTheApiAnswersWithAnEmptyBody_ReadsBackInsteadOfCreatingAgain()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        var cli = await RunCliAsync(config, CreateArgs());
        AssertReadBackAfterOneWrite("CLI", cli.Stdout);

        var mcp = await RunToolAsync(config, Invoke());
        AssertReadBackAfterOneWrite("MCP", mcp);
    }

    [Fact]
    public async Task Create_ReturnsTheSameSavedEntryDocumentOnBothSurfaces()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        var cli = await RunCliAsync(config, CreateArgs());
        var mcp = await RunToolAsync(config, Invoke());

        Golden.Canonicalize(mcp).Should().Be(Golden.Canonicalize(cli.Stdout));
    }

    [Fact]
    public async Task Create_ResolvesTheIterationNameOnBothSurfaces()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        var cli = await CliPayloadAsync(config, CreateArgs("--iteration", "Order history"));
        var mcp = await McpPayloadAsync(config, h => h.Timesheets.CreateTimesheet(
            Client, Project, Date, description: "Product search", iteration: "Order history", ct: Ct));

        mcp.Should().Be(cli);
        Field(cli, "iterationID").GetInt32().Should().Be(3403);
    }

    [Fact]
    public async Task Create_WithAnUnknownIteration_FailsOnBothSurfacesWithoutWriting()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        Arrange(null);
        var cli = await CliRunner.RunAsync(CreateArgs("--iteration", "Sprint 99"), ApiClient, config, Ct);
        var cliPaths = Paths();
        var mcp = await RunToolAsync(config, h => h.Timesheets.CreateTimesheet(
            Client, Project, Date, description: "Product search", iteration: "Sprint 99", ct: Ct));

        const string expected = "Unknown iteration 'Sprint 99' for project '1I776Q'. Available iterations: Checkout API (3402), Order history (3403).";
        cli.ExitCode.Should().NotBe(0);
        JsonDocument.Parse(cli.Stdout).RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Be(expected);
        JsonDocument.Parse(mcp).RootElement.GetProperty("error").GetString().Should().Be(expected);
        cliPaths.Should().NotContain(SaveRoute);
        Paths().Should().NotContain(SaveRoute);
    }

    private static string[] CreateArgs(params string[] extra) =>
    [
        "ts", "create", "--client", Client, "--project", Project, "--date", Date,
        "--description", "Product search", "--iteration", "3402", "--yes", "--json",
        .. extra
    ];

    private Func<McpToolHost, Task<string>> Invoke() => h => h.Timesheets.CreateTimesheet(
        Client, Project, Date, description: "Product search", iterationId: 3402, ct: Ct);

    private static Action<WireMock.Server.WireMockServer> RecentCategory(string categoryId) =>
        server => NorthwindApi.Json(server, SummaryRoute, "POST", new List<TimesheetSummaryEntry>
        {
            new()
            {
                TimeId = NorthwindApi.TimesheetId,
                TimesheetDate = Date,
                CategoryId = categoryId,
                SellPrice = 175m
            }
        }, NorthwindApi.OverridePriority);

    private static void ExpiredRate(WireMock.Server.WireMockServer server) =>
        NorthwindApi.Json(server, RateRoute, "GET", new ClientRateResponse
        {
            EmpId = NorthwindApi.EmpId,
            ClientId = Client,
            Rate = 175m,
            PrepaidRate = 150m,
            ExpiryDate = "2026-01-31"
        }, NorthwindApi.OverridePriority);

    private async Task<CliRunner.Result> RunCliAsync(
        IConfigService config, string[] args, Action<WireMock.Server.WireMockServer>? arrange = null)
    {
        Arrange(arrange);

        var result = await CliRunner.RunAsync(args, ApiClient, config, Ct);
        result.ExitCode.Should().Be(0, result.Stderr);

        return result;
    }

    private async Task<string> RunToolAsync(
        IConfigService config,
        Func<McpToolHost, Task<string>> invoke,
        Action<WireMock.Server.WireMockServer>? arrange = null)
    {
        Arrange(arrange);

        return await invoke(new McpToolHost(ApiClient, config));
    }

    private async Task<string> CliPayloadAsync(
        IConfigService config, string[] args, Action<WireMock.Server.WireMockServer>? arrange = null)
    {
        await RunCliAsync(config, args, arrange);
        return SavedPayload();
    }

    private async Task<string> McpPayloadAsync(
        IConfigService config,
        Func<McpToolHost, Task<string>> invoke,
        Action<WireMock.Server.WireMockServer>? arrange = null)
    {
        await RunToolAsync(config, invoke, arrange);
        return SavedPayload();
    }

    private void Arrange(Action<WireMock.Server.WireMockServer>? arrange)
    {
        WireMock.Reset();
        NorthwindApi.StubAll(WireMock);
        arrange?.Invoke(WireMock);
    }

    /// <summary>
    /// The write response is empty, so the saved entry can only have come from a read after the
    /// POST. Dropping the read-back leaves the document without an id and fails here.
    /// </summary>
    private void AssertReadBackAfterOneWrite(string side, string document)
    {
        var paths = Paths();
        paths.Count(p => p == SaveRoute).Should().Be(1, $"{side} must create exactly once");
        paths.FindLastIndex(p => p == ListRoute)
            .Should().BeGreaterThan(paths.IndexOf(SaveRoute), $"{side} must read the row back");

        using var doc = JsonDocument.Parse(document);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("timesheetId").GetInt32().Should().Be(NorthwindApi.TimesheetId);

        var entry = doc.RootElement.GetProperty("timesheet");
        entry.GetProperty("timeId").GetInt32().Should().Be(NorthwindApi.TimesheetId);
        entry.GetProperty("projectId").GetString().Should().Be(Project);
        entry.GetProperty("notes").GetString().Should().Be("Product search");
        entry.GetProperty("startTime").GetString().Should().Be($"{Date}T09:00:00");
    }

    private string SavedPayload() =>
        Golden.Canonicalize(WireMock.LogEntries
            .Single(e => e.RequestMessage!.AbsolutePath == SaveRoute)
            .RequestMessage!.Body!);

    private List<string> Paths() =>
        WireMock.LogEntries.Select(e => e.RequestMessage!.AbsolutePath).ToList();

    private void AssertNoWrites()
    {
        Paths().Should().NotContain(SaveRoute);
        Paths().Should().NotContain(SaveRateRoute);
    }

    private static JsonElement Field(string payload, string name) =>
        JsonDocument.Parse(payload).RootElement.GetProperty(name);
}
