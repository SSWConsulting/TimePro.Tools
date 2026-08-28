using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using SSW.TimePro.Cli.Infrastructure.ApiClient;

namespace SSW.TimePro.Cli.Integration.Features;

public class LeaveBalancesApiTests : TestBase
{
    private const string Csv = "Employee,Leave Type,Units\nJane Doe,Annual Leave,76.00\nJohn Smith,Annual Leave,12.50\n";

    [Fact]
    public async Task ImportLeaveBalances_SendsRawCsvBodyWithCsvContentType()
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/balances/import")
                .UsingPost()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "asAtDate": "2026-08-01",
                  "created": 1,
                  "updated": 2,
                  "unmatchedEmployees": ["Nobody Here"],
                  "warnings": ["Jane Doe has an unusually large balance (2500.00 hours) - please verify."]
                }
                """)
        );

        var result = await ApiClient.ImportLeaveBalancesAsync(Csv, CancellationToken.None);

        result.Should().NotBeNull();
        result!.AsAtDate.Should().Be(new DateOnly(2026, 8, 1));
        result.Created.Should().Be(1);
        result.Updated.Should().Be(2);
        result.UnmatchedEmployees.Should().Equal("Nobody Here");
        result.Warnings.Should().ContainSingle();

        // The endpoint reads the request stream as CSV. If this ever regresses to the JSON
        // helpers the body becomes an escaped string literal and the server-side parser fails.
        var request = WireMock.LogEntries.Should().ContainSingle().Subject.RequestMessage;
        request.Body.Should().Be(Csv);
        request.Headers!["Content-Type"].ToString().Should().Contain("text/csv");
    }

    [Fact]
    public async Task ImportLeaveBalances_WhenForbidden_ThrowsApiExceptionWith403()
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/balances/import")
                .UsingPost()
        ).RespondWith(
            Response.Create().WithStatusCode(403)
        );

        var act = () => ApiClient.ImportLeaveBalancesAsync(Csv, CancellationToken.None);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ImportLeaveBalances_WhenCsvRejected_ThrowsApiExceptionCarryingServerMessage()
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/balances/import")
                .UsingPost()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(422)
                .WithHeader("Content-Type", "application/json")
                .WithBody("\"Missing required column 'Units'.\"")
        );

        var act = () => ApiClient.ImportLeaveBalancesAsync(Csv, CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.StatusCode.Should().Be(422);
        exception.ResponseBody.Should().Contain("Missing required column");
    }

    [Fact]
    public async Task GetLeaveBalanceStatus_WithStoredBalances_ReturnsStatus()
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/balances/status")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "asAtDate": "2026-08-01",
                  "lastImportedAt": "2026-08-02T09:00:00+00:00",
                  "employeeCount": 42,
                  "isStale": true
                }
                """)
        );

        var status = await ApiClient.GetLeaveBalanceStatusAsync(CancellationToken.None);

        status.Should().NotBeNull();
        status!.AsAtDate.Should().Be(new DateOnly(2026, 8, 1));
        status.LastImportedAt.Should().Be(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero));
        status.EmployeeCount.Should().Be(42);
        status.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetLeaveBalanceStatus_WhenNothingImported_ReturnsNullDates()
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/balances/status")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "asAtDate": null,
                  "lastImportedAt": null,
                  "employeeCount": 0,
                  "isStale": false
                }
                """)
        );

        var status = await ApiClient.GetLeaveBalanceStatusAsync(CancellationToken.None);

        status.Should().NotBeNull();
        status!.AsAtDate.Should().BeNull();
        status.LastImportedAt.Should().BeNull();
        status.EmployeeCount.Should().Be(0);
    }
}
