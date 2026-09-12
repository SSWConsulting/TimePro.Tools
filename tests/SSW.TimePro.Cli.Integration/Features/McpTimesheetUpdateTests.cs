using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Features.Mcp.Tools;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.Config;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

/// <summary>
/// SaveTimesheet?isEdit=true replaces the whole row, so the MCP update tool has to read the entry
/// back and re-send everything the caller did not name.
/// </summary>
public class McpTimesheetUpdateTests : TestBase
{
    [Fact]
    public async Task UpdateTimesheet_WhenOnlyNotesChange_PreservesTheOmittedFieldsInThePayload()
    {
        StubLookups();
        StubSave();

        var result = await CreateTools().UpdateTimesheet(
            timesheetId: 4242,
            description: "Checkout API",
            date: "2026-03-16",
            ct: TestContext.Current.CancellationToken);

        var body = SavedPayload();
        body.GetProperty("timeID").GetInt32().Should().Be(4242);
        body.GetProperty("empID").GetString().Should().Be("TST");
        body.GetProperty("clientID").GetString().Should().Be("NWIND");
        body.GetProperty("projectID").GetString().Should().Be("1I776Q");
        body.GetProperty("iterationID").GetInt32().Should().Be(3402);
        body.GetProperty("categoryID").GetString().Should().Be("WEBDEV");
        body.GetProperty("locationID").GetString().Should().Be("Home");
        body.GetProperty("dateCreated").GetString().Should().Be("2026-03-16");
        body.GetProperty("timeStart").GetString().Should().Be("2026-03-16T09:00:00");
        body.GetProperty("timeEnd").GetString().Should().Be("2026-03-16T17:00:00");
        body.GetProperty("timeLess").GetDecimal().Should().Be(0.5m);
        body.GetProperty("billableID").GetString().Should().Be("B");
        body.GetProperty("sellPrice").GetDecimal().Should().Be(175m);
        body.GetProperty("Notes").GetString().Should().Be("Checkout API");

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("timesheetId").GetInt32().Should().Be(4242);
        doc.RootElement.GetProperty("timesheet").GetProperty("timeId").GetInt32().Should().Be(4242);
    }

    [Fact]
    public async Task UpdateTimesheet_WhenIterationIsGivenByName_ResolvesItAndKeepsTheRest()
    {
        StubLookups();
        StubSave();

        await CreateTools().UpdateTimesheet(
            timesheetId: 4242,
            iteration: "Order history",
            date: "2026-03-16",
            ct: TestContext.Current.CancellationToken);

        var body = SavedPayload();
        body.GetProperty("iterationID").GetInt32().Should().Be(3403);
        body.GetProperty("Notes").GetString().Should().Be("Product search");
        body.GetProperty("sellPrice").GetDecimal().Should().Be(175m);
    }

    [Fact]
    public async Task UpdateTimesheet_WhenEntryIsSuggested_DoesNotCallSave()
    {
        StubLookups(isSuggested: true);
        StubSave();

        var result = await CreateTools().UpdateTimesheet(
            timesheetId: 4242,
            description: "Checkout API",
            date: "2026-03-16",
            ct: TestContext.Current.CancellationToken);

        WireMock.LogEntries
            .Should().NotContain(e => e.RequestMessage!.AbsolutePath == "/api/Timesheets/SaveTimesheet");
        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("error").GetString()
            .Should().Be("Timesheet 4242 is a suggestion and cannot be updated. Accept it first: tp ts accept 4242");
    }

    private TimesheetMcpTools CreateTools()
    {
        var updates = new TimesheetUpdateService(ApiClient);
        return new TimesheetMcpTools(
            ApiClient,
            new StubConfigService(TestTenant),
            updates,
            new TimesheetAcceptService(ApiClient, updates));
    }

    private JsonElement SavedPayload()
    {
        var save = WireMock.LogEntries
            .Single(e => e.RequestMessage!.AbsolutePath == "/api/Timesheets/SaveTimesheet");
        return JsonDocument.Parse(save.RequestMessage!.Body!).RootElement.Clone();
    }

    private void StubLookups(bool isSuggested = false)
    {
        WireMock.Given(Request.Create()
                .WithPath("/api/Timesheets/GetTimesheetListViewModel")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($$"""
                [
                  {
                    "timeId": 4242,
                    "empId": "TST",
                    "client": "Northwind Traders",
                    "clientId": "NWIND",
                    "project": "Northwind Traders",
                    "projectId": "1I776Q",
                    "iteration": "Checkout API",
                    "iterationId": null,
                    "locationId": "Home",
                    "notes": "Product search",
                    "date": "2026-03-16T00:00:00",
                    "startTime": "2026-03-16T09:00:00",
                    "endTime": "2026-03-16T17:00:00",
                    "billableId": "B",
                    "less": 0.5,
                    "isSuggested": {{(isSuggested ? "true" : "false")}}
                  }
                ]
                """));

        WireMock.Given(Request.Create()
                .WithPath("/api/timesheetSummary/GetTableSummarydata")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""[{"timeId":4242,"categoryId":"WEBDEV","sellPrice":175.0}]"""));

        WireMock.Given(Request.Create()
                .WithPath("/api/ProjectIteration/GetIterationsForAddTimesheet")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                [
                  {"iterationId":3402,"iterationName":"Checkout API"},
                  {"iterationId":3403,"iterationName":"Order history"}
                ]
                """));

        WireMock.Given(Request.Create()
                .WithPath("/api/v2/clients/NWIND/taxrates")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("0.1"));
    }

    private void StubSave()
    {
        WireMock.Given(Request.Create()
                .WithPath("/api/Timesheets/SaveTimesheet")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(""));
    }

    private sealed class StubConfigService : IConfigService
    {
        private readonly TenantConfig _tenant;
        public StubConfigService(TenantConfig tenant) => _tenant = tenant;

        public string ConfigDirectory => Path.GetTempPath();
        public GlobalConfig LoadGlobalConfig() => new();
        public void SaveGlobalConfig(GlobalConfig config) { }
        public TenantConfig? LoadTenantConfig(string tenantId) => _tenant;
        public void SaveTenantConfig(TenantConfig config) { }
        public void DeleteTenantConfig(string tenantId) { }
        public TenantConfig? LoadActiveTenantConfig() => _tenant;
        public void SetActiveTenantOverride(TenantConfig tenant) { }
        public void ClearActiveTenantOverride() { }
        public List<TenantConfig> ListTenants() => [_tenant];
        public List<RepoMappingEntry> LoadRepoMappings() => [];
        public void SaveRepoMappings(List<RepoMappingEntry> mappings) { }
    }
}
