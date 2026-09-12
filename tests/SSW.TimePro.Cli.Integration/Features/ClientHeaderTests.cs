using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

public class ClientHeaderTests : TestBase
{
    private const string Route = "/api/Employees/GetEmployeeID";

    [Fact]
    public async Task EveryCall_CarriesTheClientIdentityHeaders()
    {
        StubEmployeeId();
        ClientContext.BeginCli("ts get");

        await ApiClient.GetEmployeeIdAsync(TestContext.Current.CancellationToken);

        var headers = SingleRequestHeaders();

        Header(headers, "User-Agent").Should().MatchRegex(@"^timepro-cli/[A-Za-z0-9.\-]+$");
        Header(headers, ClientHeaders.Surface).Should().Be("cli");
        Header(headers, ClientHeaders.Command).Should().Be("ts get");
        Guid.TryParse(Header(headers, ClientHeaders.RequestId), out _)
            .Should().BeTrue("the request id is a fresh GUID per HTTP attempt");
    }

    [Fact]
    public async Task TheExistingAuthHeadersAreUnchanged()
    {
        StubEmployeeId();
        ClientContext.BeginCli("ts get");

        await ApiClient.GetEmployeeIdAsync(TestContext.Current.CancellationToken);

        var headers = SingleRequestHeaders();
        Header(headers, "x-timepro-tenant-id").Should().Be(TestTenant.TenantId);
        Header(headers, "x-timepro-api-key").Should().Be(TestTenant.ApiKey);
        Header(headers, "x-timepro-api-name").Should().Be(TestTenant.AppName);
    }

    [Fact]
    public async Task EachAttemptGetsItsOwnRequestId()
    {
        StubEmployeeId();
        ClientContext.BeginCli("ts get");

        await ApiClient.GetEmployeeIdAsync(TestContext.Current.CancellationToken);
        await ApiClient.GetEmployeeIdAsync(TestContext.Current.CancellationToken);

        var ids = WireMock.LogEntries
            .Select(e => e.RequestMessage!.Headers![ClientHeaders.RequestId].Single())
            .ToList();

        ids.Should().HaveCount(2);
        ids.Should().OnlyHaveUniqueItems();
        ClientContext.Current!.Requests.Select(r => r.RequestId).Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task AFailedCall_CarriesTheServerEchoedRequestIdOnTheException()
    {
        WireMock
            .Given(Request.Create().WithPath(Route).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("x-request-id", "server-side-id")
                .WithBody("""{"detail":"boom"}"""));

        ClientContext.BeginCli("ts get");

        var act = async () => await ApiClient.GetEmployeeIdAsync(TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<ApiException>()).Which;
        failure.RequestId.Should().Be("server-side-id");
        ClientContext.Current!.Requests.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Method = "GET", Route, Status = 500 });
    }

    [Fact]
    public async Task McpToolCalls_AreLabelledAsSuchOnTheWire()
    {
        StubEmployeeId();
        ClientContext.BeginMcpTool("get_timesheets");

        await ApiClient.GetEmployeeIdAsync(TestContext.Current.CancellationToken);

        var headers = SingleRequestHeaders();
        Header(headers, ClientHeaders.Surface).Should().Be("mcp");
        Header(headers, ClientHeaders.Command).Should().Be("mcp:get_timesheets");
    }

    private void StubEmployeeId() =>
        WireMock
            .Given(Request.Create().WithPath(Route).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"empID":"BOB"}"""));

    private IDictionary<string, WireMock.Types.WireMockList<string>> SingleRequestHeaders() =>
        WireMock.LogEntries.Single().RequestMessage!.Headers!;

    private static string Header(
        IDictionary<string, WireMock.Types.WireMockList<string>> headers, string name) =>
        headers.Single(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value.Single();
}
