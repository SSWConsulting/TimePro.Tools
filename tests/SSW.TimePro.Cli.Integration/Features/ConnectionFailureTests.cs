using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

public class ConnectionFailureTests
{
    [Fact]
    public async Task UnreachableHost_ThrowsWithTenantFileAndApiUrl()
    {
        var tenant = new TenantConfig
        {
            ConfigName = "northwind-local",
            TenantId = "northwind",
            ApiUrl = "http://127.0.0.1:1/",
            ApiKey = "test-api-key"
        };

        var client = new TimeProApiClient(new HttpClient(), new FixedTenantProvider(tenant));

        var act = async () => await client.GetEmployeeIdAsync();

        var failure = (await act.Should().ThrowAsync<TimeProConnectionException>()).Which;
        failure.TenantFile.Should().Be("northwind-local");
        failure.ApiUrl.Should().Be("http://127.0.0.1:1/");
        failure.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Timeout_ThrowsWithTenantContext()
    {
        var tenant = Tenant("https://api.staging-sswtimepro.com/");
        var http = new HttpClient(new ThrowingHandler(_ => new TaskCanceledException("timeout")));
        var client = new TimeProApiClient(http, new FixedTenantProvider(tenant));

        var act = async () => await client.GetEmployeeIdAsync();

        var failure = (await act.Should().ThrowAsync<TimeProConnectionException>()).Which;
        failure.Message.Should().Contain("timed out");
        failure.Message.Should().Contain("api.staging-sswtimepro.com");
        failure.TenantFile.Should().Be("northwind-local");
        failure.ApiUrl.Should().Be("https://api.staging-sswtimepro.com/");
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutBeingReportedAsAConnectionFailure()
    {
        var http = new HttpClient(new ThrowingHandler(ct => new TaskCanceledException("cancelled", null, ct)));
        var client = new TimeProApiClient(http, new FixedTenantProvider(Tenant("https://api.staging-sswtimepro.com/")));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await client.GetEmployeeIdAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static TenantConfig Tenant(string apiUrl) =>
        new()
        {
            ConfigName = "northwind-local",
            TenantId = "northwind",
            ApiUrl = apiUrl,
            ApiKey = "test-api-key"
        };

    private sealed class FixedTenantProvider(TenantConfig tenant) : ITenantProvider
    {
        public TenantConfig? GetCurrentTenant() => tenant;
    }

    private sealed class ThrowingHandler(Func<CancellationToken, Exception> failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw failure(cancellationToken);
    }
}
