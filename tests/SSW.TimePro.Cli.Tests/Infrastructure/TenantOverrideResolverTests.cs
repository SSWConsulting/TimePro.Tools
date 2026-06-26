using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public sealed class TenantOverrideResolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _config;

    public TenantOverrideResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"timepro-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _config = new ConfigService(_tempDir);
    }

    [Fact]
    public void ExtractCommandLineOptions_RemovesTenantAndEnvironmentOptions()
    {
        var result = TenantOverrideResolver.ExtractCommandLineOptions(
            ["ts", "get", "--tenant", "northwind-eu", "--env=staging", "--json"]);

        result.Error.Should().BeNull();
        result.Args.Should().Equal("ts", "get", "--json");
        result.Options.TenantName.Should().Be("northwind-eu");
        result.Options.EnvironmentName.Should().Be("staging");
    }

    [Theory]
    [InlineData("login")]
    [InlineData("logout")]
    [InlineData("mcp")]
    public void ExtractCommandLineOptions_PreservesCommandsWithOwnTenantOption(string command)
    {
        var args = new[] { command, "--tenant", "northwind" };

        var result = TenantOverrideResolver.ExtractCommandLineOptions(args);

        result.Error.Should().BeNull();
        result.Args.Should().Equal(args);
        result.Options.HasOverride.Should().BeFalse();
    }

    [Fact]
    public void ResolveTenantOverride_UsesTenantAndEnvironmentConfigName()
    {
        SaveTenant("northwind-eu-staging");

        var tenant = TenantOverrideResolver.ResolveTenantOverride(
            _config,
            new TenantOverrideOptions("northwind-eu", "staging"),
            out var error);

        error.Should().BeNull();
        tenant.Should().NotBeNull();
        tenant!.TenantId.Should().Be("northwind-eu-staging");
    }

    [Fact]
    public void ResolveTenantOverride_UsesActiveTenantAsEnvironmentBase()
    {
        _config.SaveGlobalConfig(new GlobalConfig { ActiveTenant = "northwind-staging" });
        SaveTenant("northwind-local");

        var tenant = TenantOverrideResolver.ResolveTenantOverride(
            _config,
            new TenantOverrideOptions(null, "local"),
            out var error);

        error.Should().BeNull();
        tenant.Should().NotBeNull();
        tenant!.TenantId.Should().Be("northwind-local");
    }

    [Theory]
    [InlineData("northwind", "production", "northwind")]
    [InlineData("northwind-staging", "prod", "northwind")]
    [InlineData("northwind-local", "stage", "northwind-staging")]
    [InlineData("northwind-staging", "development", "northwind-dev")]
    public void ResolveEnvironmentTenantName_NormalizesKnownEnvironmentNames(
        string tenantName,
        string environmentName,
        string expected)
    {
        TenantOverrideResolver.ResolveEnvironmentTenantName(tenantName, environmentName)
            .Should().Be(expected);
    }

    private void SaveTenant(string tenantId)
    {
        _config.SaveTenantConfig(new TenantConfig
        {
            TenantId = tenantId,
            ApiUrl = "https://api.staging-sswtimepro.com",
            ApiKey = "test-key"
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
