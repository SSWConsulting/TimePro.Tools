using System.Text.Json;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Integration.Features;

public class LeaveListTests : TestBase
{
    [Fact]
    public async Task GetLeave_WithValidResponse_ReturnsPaginatedLeave()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/")
                .WithParam("leaveFilter", "UPCOMING")
                .WithParam("pageNumber", "1")
                .WithParam("pageSize", "10")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromFile("Fixtures/leave-list.json")
        );

        // Act
        var result = await ApiClient.GetLeaveAsync("UPCOMING", 1, 10, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Leaves.Should().NotBeNull();
        result.Leaves!.TotalItems.Should().Be(2);
        result.Leaves.Items.Should().HaveCount(2);

        var first = result.Leaves.Items[0];
        first.Id.Should().Be("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        first.RequestedEmpId.Should().Be("TST");
        first.Note.Should().Be("Annual leave for personal matters");
        first.LeaveType!.Name.Should().Be("Annual Leave");
    }

    [Fact]
    public async Task GetLeave_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        var emptyResponse = """
        {
          "leaves": {
            "pageNumber": 1,
            "pageSize": 10,
            "totalItems": 0,
            "totalPages": 0,
            "items": []
          },
          "cancelledCount": 0
        }
        """;

        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/")
                .WithParam("leaveFilter", "UPCOMING")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(emptyResponse)
        );

        // Act
        var result = await ApiClient.GetLeaveAsync("UPCOMING", 1, 10, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Leaves!.Items.Should().BeEmpty();
        result.Leaves.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task GetLeave_With401_ThrowsApiException()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(401)
                .WithBody("Unauthorized")
        );

        // Act & Assert
        var act = () => ApiClient.GetLeaveAsync("UPCOMING", 1, 10, CancellationToken.None);
        await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 401);
    }
}

public class LeaveTypesTests : TestBase
{
    [Fact]
    public async Task GetLeaveTypes_ReturnsActiveTypes()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/types")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromFile("Fixtures/leave-types.json")
        );

        // Act
        var result = await ApiClient.GetLeaveTypesAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(5);
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Annual Leave");
        result[0].IsActive.Should().BeTrue();
    }
}

public class LeaveCreateTests : TestBase
{
    [Fact]
    public async Task CreateLeave_WithValidRequest_SendsCorrectPayload()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/")
                .UsingPost()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(201)
        );

        var request = new CreateLeaveRequest
        {
            RequestedEmpId = "TST",
            StartDate = "2026-03-30T00:00:00+10:00",
            EndDate = "2026-03-30T23:59:00+10:00",
            LeaveTypeId = 1,
            Note = "Annual leave for personal matters",
            UserStartTime = "09:00:00",
            UserEndTime = "18:00:00",
            AllDay = true,
            OptionalEmp = ["colleague@test.com"],
            ApprovedBy = "admin@test.com",
            TimeLessOverride = null
        };

        // Act
        await ApiClient.CreateLeaveAsync(request, CancellationToken.None);

        // Assert - verify the request was sent
        var entries = WireMock.LogEntries;
        entries.Should().HaveCount(1);

        var logEntry = entries.First();
        logEntry.RequestMessage.Method.Should().Be("POST");
        logEntry.RequestMessage.Path.Should().Be("/api/leave/");

        // Verify headers
        logEntry.RequestMessage.Headers.Should().ContainKey("x-timepro-tenant-id");
        logEntry.RequestMessage.Headers!["x-timepro-tenant-id"].Should().Contain("test");
        logEntry.RequestMessage.Headers.Should().ContainKey("x-timepro-api-key");
        logEntry.RequestMessage.Headers!["x-timepro-api-key"].Should().Contain("test-api-key");

        // Verify body contains required fields
        var body = logEntry.RequestMessage.Body;
        body.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;

        root.GetProperty("RequestedEmpId").GetString().Should().Be("TST");
        root.GetProperty("StartDate").GetString().Should().Be("2026-03-30T00:00:00+10:00");
        root.GetProperty("EndDate").GetString().Should().Be("2026-03-30T23:59:00+10:00");
        root.GetProperty("LeaveTypeId").GetInt32().Should().Be(1);
        root.GetProperty("Note").GetString().Should().Be("Annual leave for personal matters");
        root.GetProperty("UserStartTime").GetString().Should().Be("09:00:00");
        root.GetProperty("UserEndTime").GetString().Should().Be("18:00:00");
        root.GetProperty("AllDay").GetBoolean().Should().BeTrue();
        root.GetProperty("OptionalEmp").GetArrayLength().Should().Be(1);
        root.GetProperty("ApprovedBy").GetString().Should().Be("admin@test.com");
    }

    [Fact]
    public async Task CreateLeave_WithMinimalRequest_OmitsNullFields()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/")
                .UsingPost()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(201)
        );

        var request = new CreateLeaveRequest
        {
            RequestedEmpId = "TST",
            StartDate = "2026-03-30T00:00:00+10:00",
            EndDate = "2026-03-30T23:59:00+10:00",
            LeaveTypeId = 1,
            AllDay = true
        };

        // Act
        await ApiClient.CreateLeaveAsync(request, CancellationToken.None);

        // Assert
        var body = WireMock.LogEntries.First().RequestMessage.Body;
        var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;

        // Null fields should be omitted (WriteJsonOptions uses WhenWritingNull)
        root.TryGetProperty("ApprovedBy", out _).Should().BeFalse();
        root.TryGetProperty("TimeLessOverride", out _).Should().BeFalse();

        // Non-null fields should still be present
        root.GetProperty("RequestedEmpId").GetString().Should().Be("TST");
        root.GetProperty("OptionalEmp").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CreateLeave_With409Conflict_ThrowsApiException()
    {
        // Arrange
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/")
                .UsingPost()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(409)
                .WithBody("Overlapping leave entry exists")
        );

        // Act & Assert
        var request = new CreateLeaveRequest
        {
            RequestedEmpId = "TST",
            StartDate = "2026-03-30T00:00:00+10:00",
            EndDate = "2026-03-30T23:59:00+10:00",
            LeaveTypeId = 1,
            AllDay = true
        };

        var act = () => ApiClient.CreateLeaveAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task CreateLeave_With400Validation_ThrowsApiException()
    {
        // Arrange
        var validationError = """
        {
          "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
          "title": "Validation error",
          "status": 400,
          "detail": "Validation Error has occurred",
          "StartDate": "Start date cannot be on a weekend."
        }
        """;

        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/")
                .UsingPost()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(400)
                .WithHeader("Content-Type", "application/json")
                .WithBody(validationError)
        );

        // Act & Assert
        var request = new CreateLeaveRequest
        {
            RequestedEmpId = "TST",
            StartDate = "2026-03-28T00:00:00+10:00",  // Saturday
            EndDate = "2026-03-28T23:59:00+10:00",
            LeaveTypeId = 1,
            AllDay = true
        };

        var act = () => ApiClient.CreateLeaveAsync(request, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 400);
    }
}

public class LeaveCancelTests : TestBase
{
    [Fact]
    public async Task CancelLeave_WithValidRequest_SendsCorrectPayload()
    {
        // Arrange
        var leaveId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        WireMock.Given(
            Request.Create()
                .WithPath($"/api/leave/{leaveId}/cancel")
                .UsingPut()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(204)
        );

        var request = new CancelLeaveRequest
        {
            LeaveId = leaveId,
            CancellationReason = "Plans changed"
        };

        // Act
        await ApiClient.CancelLeaveAsync(leaveId, request, CancellationToken.None);

        // Assert
        var entries = WireMock.LogEntries;
        entries.Should().HaveCount(1);

        var logEntry = entries.First();
        logEntry.RequestMessage.Method.Should().Be("PUT");
        logEntry.RequestMessage.Path.Should().Be($"/api/leave/{leaveId}/cancel");

        // Verify body contains LeaveId and CancellationReason
        var body = logEntry.RequestMessage.Body;
        body.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;

        root.GetProperty("LeaveId").GetString().Should().Be(leaveId);
        root.GetProperty("CancellationReason").GetString().Should().Be("Plans changed");
    }

    [Fact]
    public async Task CancelLeave_With404_ThrowsApiException()
    {
        // Arrange
        var leaveId = "00000000-0000-0000-0000-000000000000";
        WireMock.Given(
            Request.Create()
                .WithPath($"/api/leave/{leaveId}/cancel")
                .UsingPut()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(404)
        );

        var request = new CancelLeaveRequest
        {
            LeaveId = leaveId,
            CancellationReason = "test"
        };

        // Act & Assert
        var act = () => ApiClient.CancelLeaveAsync(leaveId, request, CancellationToken.None);
        await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 404);
    }

    [Fact]
    public async Task CancelLeave_With403Forbidden_ThrowsApiException()
    {
        // Arrange - trying to cancel someone else's leave without admin
        var leaveId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        WireMock.Given(
            Request.Create()
                .WithPath($"/api/leave/{leaveId}/cancel")
                .UsingPut()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(403)
        );

        var request = new CancelLeaveRequest
        {
            LeaveId = leaveId,
            CancellationReason = "test"
        };

        // Act & Assert
        var act = () => ApiClient.CancelLeaveAsync(leaveId, request, CancellationToken.None);
        await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 403);
    }

    [Fact]
    public async Task CancelLeave_SetsAuthHeaders()
    {
        // Arrange
        var leaveId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        WireMock.Given(
            Request.Create()
                .WithPath($"/api/leave/{leaveId}/cancel")
                .UsingPut()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(204)
        );

        var request = new CancelLeaveRequest
        {
            LeaveId = leaveId,
            CancellationReason = "test"
        };

        // Act
        await ApiClient.CancelLeaveAsync(leaveId, request, CancellationToken.None);

        // Assert
        var req = WireMock.LogEntries.First().RequestMessage;
        req.Headers.Should().ContainKey("x-timepro-tenant-id");
        req.Headers!["x-timepro-tenant-id"].Should().Contain("test");
        req.Headers.Should().ContainKey("x-timepro-api-key");
        req.Headers!["x-timepro-api-key"].Should().Contain("test-api-key");
    }
}
