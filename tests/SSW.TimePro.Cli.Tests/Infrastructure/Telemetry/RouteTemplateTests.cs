using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure.Telemetry;

public class RouteTemplateTests
{
    [Theory]
    [InlineData("/api/employees/{empId}")]
    [InlineData("/api/leave/stats/{employeeId}")]
    [InlineData("/api/v2/clients/{clientId}/taxrates")]
    public void Sanitize_KeepsAPlainTemplate(string template)
    {
        RouteTemplate.Sanitize(template, new Uri("https://api.northwind.example/api/employees/BOB"))
            .Should().Be(template);
    }

    [Theory]
    [InlineData("/api/employees/BOB\nx-injected: 1")]
    [InlineData("/api/employees/BOB?employeeID=BOB")]
    [InlineData("/api/employees/Bob Northwind")]
    [InlineData("not-a-route")]
    public void Sanitize_DiscardsATemplateThatIsNotAPlainRoute(string template)
    {
        RouteTemplate.Sanitize(template, new Uri("https://api.northwind.example/api/employees/BOB"))
            .Should().Be(RouteTemplate.Unknown);
    }

    [Fact]
    public void Sanitize_FallsBackToTheLiteralPath_WhenNoTemplateWasDeclared()
    {
        RouteTemplate.Sanitize(null, new Uri("https://api.northwind.example/api/leave/types"))
            .Should().Be("/api/leave/types");
    }

    [Fact]
    public void FromLiteralPath_DropsQueryParametersThatCarryEmployeeAndClientIds()
    {
        var route = RouteTemplate.FromLiteralPath(new Uri(
            "https://api.northwind.example/api/Timesheets/GetTimesheetListViewModel?employeeID=BOB&date=2026-03-30"));

        route.Should().Be("/api/Timesheets/GetTimesheetListViewModel");
        route.Should().NotContain("BOB");
    }

    [Theory]
    [InlineData("https://api.northwind.example/api/leave/", "/api/leave")]
    [InlineData("https://api.northwind.example/api/recurring/invoices/", "/api/recurring/invoices")]
    public void FromLiteralPath_NormalizesATrailingSlash(string url, string expected)
    {
        RouteTemplate.FromLiteralPath(new Uri(url)).Should().Be(expected);
    }

    [Fact]
    public void FromLiteralPath_ReportsUnknown_ForAMissingUri()
    {
        RouteTemplate.FromLiteralPath(null).Should().Be(RouteTemplate.Unknown);
    }
}
