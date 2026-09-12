using System.Text.Json;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// A throwaway <c>~/.config/timepro-cli</c> handed to the child CLI through
/// <see cref="ConfigPaths.ConfigDirEnvVar"/>. Stdio tests must never read or write the
/// developer's real config.
/// </summary>
public sealed class IsolatedConfigDirectory : IDisposable
{
    public const string TenantName = "northwind-test";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Root { get; }

    public IsolatedConfigDirectory(string apiUrl, bool accountingEnabled)
    {
        Root = Path.Combine(Path.GetTempPath(), "tp-mcp-harness", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(Root, "tenants"));

        var global = new GlobalConfig { ActiveTenant = TenantName };
        if (accountingEnabled)
            global.Features[FeatureCatalog.Accounting] = new FeatureConfig { Enabled = true, Version = 1 };

        File.WriteAllText(
            Path.Combine(Root, "config.json"),
            JsonSerializer.Serialize(global, JsonOptions));

        File.WriteAllText(
            Path.Combine(Root, "tenants", $"{TenantName}.json"),
            JsonSerializer.Serialize(
                new TenantConfig
                {
                    TenantId = "northwind",
                    ApiUrl = apiUrl,
                    ApiKey = "test-api-key",
                    EmployeeId = "BOB",
                    EmployeeName = "Bob Northwind"
                },
                JsonOptions));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
    }
}
