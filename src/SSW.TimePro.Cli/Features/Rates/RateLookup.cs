using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Rates;

/// <summary>
/// The rate read behind both <c>rate get</c> and the MCP GetClientRate tool. Only the projection
/// differs: the CLI answers a miss with the <see cref="RateLookupResult"/> shape while MCP still
/// returns the raw response, and aligning those is a 0.4.0 contract change rather than a patch.
/// </summary>
public static class RateLookup
{
    public static DateOnly ResolveDate(string? date) =>
        date is not null
            ? DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Today);

    public static Task<ClientRateResponse?> FetchAsync(
        ITimeProApiClient api,
        string empId,
        string clientId,
        DateOnly date,
        CancellationToken ct = default) =>
        api.GetClientRateAsync(empId, clientId, date, ct);
}
