using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using SSW.TimePro.Cli.Infrastructure.ApiClient;

namespace SSW.TimePro.Cli.Integration.Features;

public class LeaveStatsTests : TestBase
{
    [Fact]
    public async Task GetLeaveStats_WithValidResponse_DeserializesSummary()
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/stats/TST")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"daysSinceLastLeave":14,"leaveTakenInLast12Months":18.38}""")
        );

        var result = await ApiClient.GetLeaveStatsAsync("TST", CancellationToken.None);

        result.Should().NotBeNull();
        result!.DaysSinceLastLeave.Should().Be(14);
        result.LeaveTakenInLast12Months.Should().Be(18.38m);
    }

    [Fact]
    public async Task GetLeaveStats_OnServerError_ThrowsApiExceptionWith500()
    {
        WireMock.Given(
            Request.Create()
                .WithPath("/api/leave/stats/TST")
                .UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"detail":"boom"}""")
        );

        var act = async () => await ApiClient.GetLeaveStatsAsync("TST", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(500);
    }
}
