using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Infrastructure.Config;

/// <summary>
/// Per-tenant configuration stored at ~/.config/timepro-cli/tenants/{id}.json.
/// </summary>
public class TenantConfig
{
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("apiUrl")]
    public string ApiUrl { get; set; } = "https://api.sswtimepro.com";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("employeeId")]
    public string? EmployeeId { get; set; }

    [JsonPropertyName("employeeName")]
    public string? EmployeeName { get; set; }

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "SSW-TimePro-CLI";

    /// <summary>
    /// Returns the URL where the user can find their API token.
    /// </summary>
    public string GetTokenPageUrl()
    {
        return $"https://{TenantId}.sswtimepro.com/b/admin/api-key";
    }

    /// <summary>
    /// Whether this is pointing at a production API.
    /// </summary>
    [JsonIgnore]
    public bool IsProduction =>
        ApiUrl.Contains("api.sswtimepro.com", StringComparison.OrdinalIgnoreCase)
        && !ApiUrl.Contains("staging", StringComparison.OrdinalIgnoreCase)
        && !ApiUrl.Contains("local", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Safe-to-display view that omits secrets such as the API key.
    /// </summary>
    public TenantConfigSummary ToSummary()
    {
        return new TenantConfigSummary
        {
            TenantId = TenantId,
            ApiUrl = ApiUrl,
            EmployeeId = EmployeeId,
            EmployeeName = EmployeeName,
            AppName = AppName,
            IsProduction = IsProduction
        };
    }
}

public class TenantConfigSummary
{
    public string TenantId { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string AppName { get; set; } = string.Empty;
    public bool IsProduction { get; set; }
}
