using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Shared.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

public class TimesheetSaveTaxTests : TestBase
{
    [Fact]
    public async Task CreateTimesheet_WhenSalesTaxPctNotSet_LooksUpClientTaxRate()
    {
        WireMock.Given(Request.Create()
                .WithPath("/api/v2/clients/SSW/taxrates")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("0.1"));

        WireMock.Given(Request.Create()
                .WithPath("/api/Timesheets/SaveTimesheet")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"Success\":true,\"TimesheetId\":123}"));

        var request = new TimesheetRequest
        {
            EmpId = "TST",
            ClientId = "SSW",
            BillableId = "W"
        };

        await ApiClient.CreateTimesheetAsync(request);

        var saveCall = WireMock.LogEntries
            .Single(e => e.RequestMessage.AbsolutePath == "/api/Timesheets/SaveTimesheet");
        using var doc = JsonDocument.Parse(saveCall.RequestMessage.Body!);
        doc.RootElement.GetProperty("salesTaxPct").GetDecimal().Should().Be(0.1m);

        WireMock.LogEntries
            .Should().Contain(e => e.RequestMessage.AbsolutePath == "/api/v2/clients/SSW/taxrates");
    }

    [Fact]
    public async Task UpdateTimesheet_WhenSalesTaxPctNotSet_LooksUpClientTaxRate()
    {
        WireMock.Given(Request.Create()
                .WithPath("/api/v2/clients/LR8R0L/taxrates")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("0.1"));

        WireMock.Given(Request.Create()
                .WithPath("/api/Timesheets/SaveTimesheet")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"Success\":true,\"TimesheetId\":456}"));

        var request = new TimesheetRequest
        {
            EmpId = "TST",
            ClientId = "LR8R0L",
            BillableId = "BPP",
            TimeId = 456
        };

        await ApiClient.UpdateTimesheetAsync(request);

        var saveCall = WireMock.LogEntries
            .Single(e => e.RequestMessage.AbsolutePath == "/api/Timesheets/SaveTimesheet");
        using var doc = JsonDocument.Parse(saveCall.RequestMessage.Body!);
        doc.RootElement.GetProperty("salesTaxPct").GetDecimal().Should().Be(0.1m);
    }

    [Fact]
    public async Task CreateTimesheet_WhenSalesTaxPctProvided_DoesNotLookUpClient()
    {
        WireMock.Given(Request.Create()
                .WithPath("/api/Timesheets/SaveTimesheet")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"Success\":true,\"TimesheetId\":789}"));

        var request = new TimesheetRequest
        {
            EmpId = "TST",
            ClientId = "SSW",
            BillableId = "W",
            SalesTaxPct = 0.15m
        };

        await ApiClient.CreateTimesheetAsync(request);

        WireMock.LogEntries
            .Should().NotContain(e => e.RequestMessage.AbsolutePath.Contains("/taxrates"));

        var saveCall = WireMock.LogEntries
            .Single(e => e.RequestMessage.AbsolutePath == "/api/Timesheets/SaveTimesheet");
        using var doc = JsonDocument.Parse(saveCall.RequestMessage.Body!);
        doc.RootElement.GetProperty("salesTaxPct").GetDecimal().Should().Be(0.15m);
    }

    [Fact]
    public async Task CreateTimesheet_WhenClientIdMissing_SkipsLookup()
    {
        WireMock.Given(Request.Create()
                .WithPath("/api/Timesheets/SaveTimesheet")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"Success\":true,\"TimesheetId\":1}"));

        var request = new TimesheetRequest { EmpId = "TST", ClientId = "" };

        await ApiClient.CreateTimesheetAsync(request);

        WireMock.LogEntries
            .Should().NotContain(e => e.RequestMessage.AbsolutePath.Contains("/taxrates"));
    }
}
