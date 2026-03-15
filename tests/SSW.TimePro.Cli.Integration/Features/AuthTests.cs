using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

public class AuthTests : TestBase
{
    [Fact]
    public async Task GetEmployeeId_ReturnsEmployeeId()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/Employees/GetEmployeeID")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromFile("Fixtures/employee-id.json")
        );

        // Act
        var result = await ApiClient.GetEmployeeIdAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.EmpId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsUserInfo()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/v2/users/me")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromFile("Fixtures/user-me.json")
        );

        // Act
        var result = await ApiClient.GetCurrentUserAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.EmployeeId.Should().NotBeNullOrEmpty();
    }
}
