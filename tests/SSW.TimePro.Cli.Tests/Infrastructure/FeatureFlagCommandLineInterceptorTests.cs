using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class FeatureFlagCommandLineInterceptorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _config;

    public FeatureFlagCommandLineInterceptorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"timepro-cli-feature-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _config = new ConfigService(_tempDir);
    }

    [Fact]
    public void ExtractCommandLineOptions_RemovesLegacyFeatureFlags()
    {
        var result = FeatureFlagCommandLineInterceptor.ExtractCommandLineOptions(
            ["skills", "create", ".agents", "--accounting", "--dev"]);

        result.Args.Should().Equal("skills", "create", ".agents");
        result.EnableFeatures.Select(f => f.Key).Should().BeEquivalentTo([
            FeatureCatalog.Accounting,
            FeatureCatalog.Developer,
        ]);
    }

    [Fact]
    public void ExtractCommandLineOptions_DoesNotConsumeFlagsAfterDoubleDash()
    {
        var result = FeatureFlagCommandLineInterceptor.ExtractCommandLineOptions(
            ["query", "--", "--accounting"]);

        result.Args.Should().Equal("query", "--", "--accounting");
        result.EnableFeatures.Should().BeEmpty();
    }

    [Fact]
    public void EnableRequestedFeatures_PersistsEnabledFeatureAndVersion()
    {
        var result = FeatureFlagCommandLineInterceptor.ExtractCommandLineOptions(["mcp", "--accounting"]);

        FeatureFlagCommandLineInterceptor.EnableRequestedFeatures(_config, result.EnableFeatures);

        var global = _config.LoadGlobalConfig();
        global.IsFeatureEnabled(FeatureCatalog.Accounting).Should().BeTrue();
        global.Features[FeatureCatalog.Accounting].Version.Should().Be(1);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
