using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Integration.Mcp;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

[Collection(CliConsoleCollection.Name)]
public sealed class LoginConfigTests : TestBase, IDisposable
{
    private const string StagingUrl = "https://api.staging-sswtimepro.com";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tp-login-tests", Guid.NewGuid().ToString("n"));
    private readonly ConfigService _config;

    public LoginConfigTests()
    {
        _config = new ConfigService(_root);
        NorthwindApi.StubAll(WireMock);
    }

    private string TenantFile(string name) => Path.Combine(_root, "tenants", $"{name}.json");

    [Fact]
    public async Task LoginToAlias_WritesOnlyTheAliasFile_AndActivatesTheAlias()
    {
        _config.SaveGlobalConfig(new GlobalConfig { ActiveTenant = "northwind" });
        WriteTenant("northwind", "https://api.sswtimepro.com", "prod-key");
        WriteTenant("northwind-staging", StagingUrl, "old-staging-key");
        var prodBytes = File.ReadAllBytes(TenantFile("northwind"));

        var result = await CliRunner.RunAsync(
            ["login", "--tenant", "northwind-staging", "--token", "new-staging-key"],
            ApiClient, _config, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, result.Stdout + result.Stderr);
        File.ReadAllBytes(TenantFile("northwind")).Should().Equal(prodBytes);
        Directory.GetFiles(Path.Combine(_root, "tenants")).Select(Path.GetFileName)
            .Should().BeEquivalentTo("northwind.json", "northwind-staging.json");

        var alias = ReadTenant("northwind-staging");
        alias.TenantId.Should().Be("northwind");
        alias.ApiUrl.Should().Be(StagingUrl);
        alias.ApiKey.Should().Be("new-staging-key");
        alias.EmployeeId.Should().Be(NorthwindApi.EmpId);
        _config.LoadGlobalConfig().ActiveTenant.Should().Be("northwind-staging");
    }

    [Fact]
    public async Task LoginToNewTenant_WritesTheTenantFile_AndActivatesIt()
    {
        var result = await CliRunner.RunAsync(
            ["login", "--tenant", "Northwind", "--token", "new-key", "--api-url", StagingUrl],
            ApiClient, _config, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, result.Stdout + result.Stderr);
        Directory.GetFiles(Path.Combine(_root, "tenants")).Select(Path.GetFileName)
            .Should().Equal("northwind.json");

        var tenant = ReadTenant("northwind");
        tenant.TenantId.Should().Be("northwind");
        tenant.ApiKey.Should().Be("new-key");
        _config.LoadGlobalConfig().ActiveTenant.Should().Be("northwind");
    }

    private void WriteTenant(string fileName, string apiUrl, string apiKey)
    {
        Directory.CreateDirectory(Path.Combine(_root, "tenants"));
        File.WriteAllText(
            TenantFile(fileName),
            $$"""{"tenantId":"northwind","apiUrl":"{{apiUrl}}","apiKey":"{{apiKey}}"}""");
    }

    private TenantConfig ReadTenant(string fileName) =>
        JsonSerializer.Deserialize<TenantConfig>(File.ReadAllText(TenantFile(fileName)))!;

    void IDisposable.Dispose()
    {
        Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
