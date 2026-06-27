using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using System.Text.Json;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _service;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"timepro-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new ConfigService(_tempDir);
    }

    [Fact]
    public void LoadGlobalConfig_WhenFileDoesNotExist_ReturnsDefault()
    {
        var config = _service.LoadGlobalConfig();
        config.Should().NotBeNull();
        config.ActiveTenant.Should().BeNull();
        config.WfhDays.Should().BeEmpty();
        config.DefaultLocation.Should().Be("Office");
    }

    [Fact]
    public void SaveAndLoadGlobalConfig_RoundTrips()
    {
        var config = new GlobalConfig
        {
            ActiveTenant = "ssw",
            WfhDays = ["Monday", "Tuesday"],
            DefaultLocation = "Home",
            Features =
            {
                [FeatureCatalog.Accounting] = new FeatureConfig { Enabled = true, Version = 1 }
            }
        };

        _service.SaveGlobalConfig(config);
        var loaded = _service.LoadGlobalConfig();

        loaded.ActiveTenant.Should().Be("ssw");
        loaded.WfhDays.Should().BeEquivalentTo(["Monday", "Tuesday"]);
        loaded.DefaultLocation.Should().Be("Home");
        loaded.IsFeatureEnabled(FeatureCatalog.Accounting).Should().BeTrue();
        loaded.Features[FeatureCatalog.Accounting].Version.Should().Be(1);
    }

    [Fact]
    public void SaveAndLoadTenantConfig_RoundTrips()
    {
        var tenant = new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://api.staging-sswtimepro.com",
            ApiKey = "test-key-123",
            EmployeeId = "TST",
            EmployeeName = "Test User"
        };

        _service.SaveTenantConfig(tenant);
        var loaded = _service.LoadTenantConfig("test");

        loaded.Should().NotBeNull();
        loaded!.TenantId.Should().Be("test");
        loaded.ApiKey.Should().Be("test-key-123");
        loaded.EmployeeId.Should().Be("TST");
        loaded.EmployeeName.Should().Be("Test User");
    }

    [Fact]
    public void LoadTenantConfig_WhenNotExists_ReturnsNull()
    {
        var result = _service.LoadTenantConfig("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public void LoadActiveTenantConfig_WhenOverrideIsSet_ReturnsOverrideWithoutChangingGlobalConfig()
    {
        _service.SaveGlobalConfig(new GlobalConfig { ActiveTenant = "northwind" });
        _service.SaveTenantConfig(new TenantConfig
        {
            TenantId = "northwind",
            ApiUrl = "https://api.sswtimepro.com",
            ApiKey = "prod-key"
        });

        var stagingTenant = new TenantConfig
        {
            TenantId = "northwind-staging",
            ApiUrl = "https://api.staging-sswtimepro.com",
            ApiKey = "staging-key"
        };

        _service.SetActiveTenantOverride(stagingTenant);

        _service.LoadActiveTenantConfig().Should().BeSameAs(stagingTenant);
        _service.LoadGlobalConfig().ActiveTenant.Should().Be("northwind");

        _service.ClearActiveTenantOverride();

        _service.LoadActiveTenantConfig()!.TenantId.Should().Be("northwind");
    }

    [Fact]
    public void TenantConfig_IsProduction_DetectsCorrectly()
    {
        new TenantConfig { ApiUrl = "https://api.sswtimepro.com" }.IsProduction.Should().BeTrue();
        new TenantConfig { ApiUrl = "https://api.staging-sswtimepro.com" }.IsProduction.Should().BeFalse();
        new TenantConfig { ApiUrl = "https://api.local-sswtimepro.com:7107" }.IsProduction.Should().BeFalse();
    }

    [Fact]
    public void TenantConfig_GetTokenPageUrl_FormatsCorrectly()
    {
        var tenant = new TenantConfig { TenantId = "ssw" };
        tenant.GetTokenPageUrl().Should().Be("https://ssw.sswtimepro.com/b/admin/api-key");
    }

    [Fact]
    public void TenantConfig_ToSummary_OmitsApiKeyWhenSerialized()
    {
        var tenant = new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://api.sswtimepro.com",
            ApiKey = "super-secret",
            EmployeeId = "TST",
            EmployeeName = "Test User",
            AppName = "SSW-TimePro-CLI"
        };

        var summary = tenant.ToSummary();
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        json.Should().Contain("\"tenantId\":\"test\"");
        json.Should().Contain("\"apiUrl\":\"https://api.sswtimepro.com\"");
        json.Should().Contain("\"employeeId\":\"TST\"");
        json.Should().Contain("\"employeeName\":\"Test User\"");
        json.Should().NotContain("apiKey");
        summary.IsProduction.Should().BeTrue();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
