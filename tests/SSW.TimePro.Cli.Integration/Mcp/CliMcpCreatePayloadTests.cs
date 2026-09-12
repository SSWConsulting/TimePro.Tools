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
[Collection(CliConsole.Collection)]
public class CliMcpCreatePayloadTests : TestBase
{
    private const string Client = NorthwindApi.ClientId;
    private const string Project = NorthwindApi.ProjectId;
    private const string Date = NorthwindApi.AnyDate;
    private const string SaveRoute = "/api/Timesheets/SaveTimesheet";
    private const string RateRoute = "/api/Timesheets/GetClientRate";
    private const string SaveRateRoute = "/api/Timesheets/SaveClientRate";
    private const string SummaryRoute = "/api/timesheetSummary/GetTableSummarydata";

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
    public async Task Create_WhenTheApiAnswersWithAnEmptyBody_ReadsBackInsteadOfCreatingAgain()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        await CliPayloadAsync(config, CreateArgs());
        Paths().Count(p => p == SaveRoute).Should().Be(1);

        var mcp = await McpPayloadAsync(config, Invoke());
        Paths().Count(p => p == SaveRoute).Should().Be(1);
        mcp.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_ReturnsTheSavedEntryOnBothSurfaces()
    {
        var config = McpToolCatalog.Config(WireMock.Url!);

        NorthwindApi.StubAll(WireMock);
        var cli = await CliRunner.RunAsync(CreateArgs(), ApiClient, config, Ct);
        WireMock.Reset();
        NorthwindApi.StubAll(WireMock);
        var mcp = await new McpToolHost(ApiClient, config).Timesheets.CreateTimesheet(
            Client, Project, Date, description: "Product search", iterationId: 3402, ct: Ct);

        Golden.Canonicalize(mcp).Should().Be(Golden.Canonicalize(cli.Stdout));
        using var doc = JsonDocument.Parse(mcp);
        doc.RootElement.GetProperty("timesheetId").GetInt32().Should().Be(NorthwindApi.TimesheetId);
        doc.RootElement.GetProperty("timesheet").GetProperty("timeId").GetInt32()
            .Should().Be(NorthwindApi.TimesheetId);
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

    private async Task<string> CliPayloadAsync(
        IConfigService config, string[] args, Action<WireMock.Server.WireMockServer>? arrange = null)
    {
        WireMock.Reset();
        NorthwindApi.StubAll(WireMock);
        arrange?.Invoke(WireMock);

        var result = await CliRunner.RunAsync(args, ApiClient, config, Ct);
        result.ExitCode.Should().Be(0, result.Stderr);

        return SavedPayload();
    }

    private async Task<string> McpPayloadAsync(
        IConfigService config,
        Func<McpToolHost, Task<string>> invoke,
        Action<WireMock.Server.WireMockServer>? arrange = null)
    {
        WireMock.Reset();
        NorthwindApi.StubAll(WireMock);
        arrange?.Invoke(WireMock);

        await invoke(new McpToolHost(ApiClient, config));

        return SavedPayload();
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
