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
        var tenant = new TenantConfig
        {
            ConfigName = "northwind-local",
            TenantId = "northwind",
            ApiUrl = "http://10.255.255.1/",
            ApiKey = "test-api-key"
        };

        var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(200) };
        var client = new TimeProApiClient(http, new FixedTenantProvider(tenant));

        var act = async () => await client.GetEmployeeIdAsync();

        var failure = (await act.Should().ThrowAsync<TimeProConnectionException>()).Which;
        failure.TenantFile.Should().Be("northwind-local");
        failure.ApiUrl.Should().Be("http://10.255.255.1/");
    }

    private sealed class FixedTenantProvider(TenantConfig tenant) : ITenantProvider
    {
        public TenantConfig? GetCurrentTenant() => tenant;
    }
}
