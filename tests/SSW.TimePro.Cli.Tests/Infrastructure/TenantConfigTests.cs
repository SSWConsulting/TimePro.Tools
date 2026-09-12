using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class TenantConfigTests
{
    [Theory]
    [InlineData("https://api.sswtimepro.com")]
    [InlineData("https://api.sswtimepro.com/")]
    [InlineData("https://API.SSWTimePro.com")]
    public void IsProduction_IsTrue_ForTheProductionHost(string apiUrl)
    {
        Tenant(apiUrl).IsProduction.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://api.staging-sswtimepro.com")]
    [InlineData("https://localhost:5001")]
    [InlineData("https://northwind.local-sswtimepro.com")]
    [InlineData("not-a-url")]
    public void IsProduction_IsFalse_ForNonProductionHosts(string apiUrl)
    {
        Tenant(apiUrl).IsProduction.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://127.0.0.1/api.sswtimepro.com")]
    [InlineData("https://localhost:5001/?host=api.sswtimepro.com")]
    public void IsProduction_IsFalse_WhenTheProductionHostOnlyAppearsInPathOrQuery(string apiUrl)
    {
        Tenant(apiUrl).IsProduction.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://api.sswtimepro.com.northwind.example")]
    [InlineData("https://evil-api.sswtimepro.com")]
    public void IsProduction_IsFalse_ForHostnamesThatMerelyContainTheProductionHost(string apiUrl)
    {
        Tenant(apiUrl).IsProduction.Should().BeFalse();
    }

    private static TenantConfig Tenant(string apiUrl) =>
        new() { TenantId = "northwind", ApiUrl = apiUrl, ApiKey = "test-key" };
}
