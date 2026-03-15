using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

public class TimesheetGetTests : TestBase
{
    [Fact]
    public async Task GetTimesheets_WithValidResponse_ReturnsTimesheets()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/Timesheets/GetTimesheetListViewModel")
                .WithParam("employeeID", "TST")
                .WithParam("date", "2026-03-12")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromFile("Fixtures/timesheets-day.json")
        );

        // Act
        var result = await ApiClient.GetTimesheetsAsync("TST", new DateOnly(2026, 3, 12), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task GetTimesheets_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/Timesheets/GetTimesheetListViewModel")
                .WithParam("employeeID", "TST")
                .WithParam("date", "2026-03-15")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("[]")
        );

        // Act
        var result = await ApiClient.GetTimesheetsAsync("TST", new DateOnly(2026, 3, 15), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTimesheets_SetsAuthHeaders()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/Timesheets/GetTimesheetListViewModel")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("[]")
        );

        // Act
        await ApiClient.GetTimesheetsAsync("TST", new DateOnly(2026, 3, 12), CancellationToken.None);

        // Assert - verify headers were sent
        var entries = WireMock.LogEntries;
        entries.Should().HaveCount(1);
        var req = entries.First().RequestMessage;
        req.Headers.Should().ContainKey("x-timepro-tenant-id");
        req.Headers!["x-timepro-tenant-id"].Should().Contain("test");
        req.Headers.Should().ContainKey("x-timepro-api-key");
        req.Headers!["x-timepro-api-key"].Should().Contain("test-api-key");
    }

    [Fact]
    public async Task GetTimesheets_With401_ThrowsApiException()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/Timesheets/GetTimesheetListViewModel")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(401)
                .WithBody("Unauthorized")
        );

        // Act & Assert
        var act = () => ApiClient.GetTimesheetsAsync("TST", new DateOnly(2026, 3, 12), CancellationToken.None);
        await act.Should().ThrowAsync<Infrastructure.ApiClient.ApiException>()
            .Where(e => e.StatusCode == 401);
    }
}
