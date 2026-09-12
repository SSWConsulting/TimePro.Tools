using FluentAssertions;
using SSW.TimePro.Cli.Features.Tenants;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Tenants;

public class TenantListItemTests
{
    [Fact]
    public void From_MarksActiveByConfigFileName_NotTenantId()
    {
        var prod = Tenant("northwind", "https://api.sswtimepro.com");
        var staging = Tenant("northwind-staging", "https://api.staging-sswtimepro.com");

        TenantListItem.From(prod, "northwind-staging").IsActive.Should().BeFalse();
        TenantListItem.From(staging, "northwind-staging").IsActive.Should().BeTrue();
    }

    [Fact]
    public void From_ExposesFileApiUrlAndEnvironment()
    {
        var item = TenantListItem.From(Tenant("northwind-staging", "https://api.staging-sswtimepro.com"), null);

        item.File.Should().Be("northwind-staging");
        item.TenantId.Should().Be("northwind");
        item.ApiUrl.Should().Be("https://api.staging-sswtimepro.com");
        item.IsProduction.Should().BeFalse();
        item.IsActive.Should().BeFalse();
    }

    [Fact]
    public void From_FlagsProductionApiUrl()
    {
        TenantListItem.From(Tenant("northwind", "https://api.sswtimepro.com"), "northwind")
            .IsProduction.Should().BeTrue();
    }

    private static TenantConfig Tenant(string configName, string apiUrl) =>
        new()
        {
            ConfigName = configName,
            TenantId = "northwind",
            ApiUrl = apiUrl,
            ApiKey = "test-key",
            EmployeeId = "BOB",
            EmployeeName = "Bob Northwind"
        };
}
