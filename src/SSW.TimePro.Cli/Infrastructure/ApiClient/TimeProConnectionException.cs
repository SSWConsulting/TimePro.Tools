namespace SSW.TimePro.Cli.Infrastructure.ApiClient;

/// <summary>
/// The API host could not be reached (connection refused, timeout, DNS failure).
/// Carries the tenant that produced the URL, because the usual fix is switching tenant.
/// </summary>
public sealed class TimeProConnectionException : Exception
{
    public TimeProConnectionException(
        string message,
        string? tenantFile,
        string? tenantId,
        string apiUrl,
        Exception? innerException = null)
        : base(message, innerException)
    {
        TenantFile = tenantFile;
        TenantId = tenantId;
        ApiUrl = apiUrl;
    }

    public string? TenantFile { get; }
    public string? TenantId { get; }
    public string ApiUrl { get; }
}
