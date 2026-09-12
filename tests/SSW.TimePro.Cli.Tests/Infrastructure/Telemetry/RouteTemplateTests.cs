using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure.Telemetry;

public class RouteTemplateTests
{
    [Theory]
    [InlineData("https://api.northwind.example/api/v2/ClientInvoice/48219/receipts", "/api/v2/ClientInvoice/{id}/receipts")]
    [InlineData("https://api.northwind.example/api/leave/6f3e1f2e-0b6f-4f0a-9f4e-2f5a1c8d9b77/cancel", "/api/leave/{id}/cancel")]
    [InlineData("https://api.northwind.example/api/Timesheets/SaveTimesheet?isEdit=true", "/api/Timesheets/SaveTimesheet")]
    public void From_ReplacesIdentifyingSegmentsAndDropsTheQuery(string url, string expected)
    {
        RouteTemplate.From(new Uri(url)).Should().Be(expected);
    }

    [Fact]
    public void From_DropsQueryParametersThatCarryEmployeeAndClientIds()
    {
        var route = RouteTemplate.From(new Uri(
            "https://api.northwind.example/api/Timesheets/GetTimesheetListViewModel?employeeID=BOB&date=2026-03-30"));

        route.Should().Be("/api/Timesheets/GetTimesheetListViewModel");
        route.Should().NotContain("BOB");
    }

    [Fact]
    public void From_KeepsNonNumericSegments()
    {
        RouteTemplate.From(new Uri("https://api.northwind.example/api/leave/balances/status"))
            .Should().Be("/api/leave/balances/status");
    }

    [Fact]
    public void From_ReportsUnknown_ForAMissingUri()
    {
        RouteTemplate.From(null).Should().Be("unknown");
    }
}
