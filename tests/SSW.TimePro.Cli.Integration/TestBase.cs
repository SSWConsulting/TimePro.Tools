using System.Text.Json;
using WireMock.Server;
using WireMock.Settings;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Integration;

public abstract class TestBase : IDisposable
{
    protected readonly WireMockServer WireMock;
    protected readonly TimeProApiClient ApiClient;
    protected readonly TenantConfig TestTenant;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    protected TestBase()
    {
        WireMock = WireMockServer.Start(new WireMockServerSettings
        {
            Port = 0  // Random available port
        });

        TestTenant = new TenantConfig
        {
            TenantId = "test",
            ApiUrl = WireMock.Url!,
            ApiKey = "test-api-key",
            EmployeeId = "TST",
            EmployeeName = "Test User"
        };

        // Create a tenant provider that always returns our test tenant
        var tenantProvider = new TestTenantProvider(TestTenant);
        var httpClient = new HttpClient();
        ApiClient = new TimeProApiClient(httpClient, tenantProvider);
    }

    protected string LoadFixture(string name)
    {
        var path = Path.Combine("Fixtures", name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}");
        return File.ReadAllText(path);
    }

    protected static string ToJson<T>(T obj) =>
        JsonSerializer.Serialize(obj, JsonOptions);

    public void Dispose()
    {
        WireMock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Simple tenant provider for tests that always returns a fixed tenant.
    /// </summary>
    private class TestTenantProvider : ITenantProvider
    {
        private readonly TenantConfig _tenant;
        public TestTenantProvider(TenantConfig tenant) => _tenant = tenant;
        public TenantConfig? GetCurrentTenant() => _tenant;
    }
}
